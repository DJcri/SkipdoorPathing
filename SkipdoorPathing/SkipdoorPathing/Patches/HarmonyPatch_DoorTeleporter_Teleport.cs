using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace SkipdoorPathing
{
    [StaticConstructorOnStartup]
    public static class PatchInitializer
    {
        static PatchInitializer()
        {
            var harmony = new Harmony("DCSzar.SkipdoorPathing.DoorTeleporterOptimization");

            // VEF type
            var type = AccessTools.TypeByName("VEF.Buildings.DoorTeleporter");
            if (type == null) return;

            // Teleport(Thing thing, Map mapTarget, IntVec3 cellTarget)
            var original = AccessTools.Method(type, "Teleport", new[] { typeof(Thing), typeof(Map), typeof(IntVec3) });
            if (original == null) return;

            var transpiler = AccessTools.Method(typeof(DoorTeleporter_Teleport_Logic), nameof(DoorTeleporter_Teleport_Logic.Transpiler));
            harmony.Patch(original, transpiler: new HarmonyMethod(transpiler));
        }
    }

    public static class DoorTeleporter_Teleport_Logic
    {
        /// <summary>
        /// Same-map pawn teleports can be done by in-place reposition + Notify_Teleported.
        /// Return true to skip the heavy ExitMap/Spawn logic.
        /// </summary>
        public static bool TryRepositionIfPawn(Thing thing, Map mapTarget, IntVec3 cellTarget)
        {
            if (!(thing is Pawn pawn)) return false;
            if (pawn.Map != mapTarget) return false;

            // Some systems look at this flag
            pawn.teleporting = true;

            pawn.Position = cellTarget;

            // Refresh drawer, pather caches, etc.
            pawn.Notify_Teleported();

            // Reachability caches can become stale after teleports
            mapTarget.reachability.ClearCache();

            pawn.teleporting = false;
            return true;
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var codes = new List<CodeInstruction>(instructions);

            var tryRepositionMethod = AccessTools.Method(typeof(DoorTeleporter_Teleport_Logic), nameof(TryRepositionIfPawn));
            var doorTeleporterType = AccessTools.TypeByName("VEF.Buildings.DoorTeleporter");
            var teleportEffectersField = doorTeleporterType != null ? AccessTools.Field(doorTeleporterType, "teleportEffecters") : null;

            if (tryRepositionMethod == null || teleportEffectersField == null)
            {
                foreach (var ci in codes) yield return ci;
                yield break;
            }

            // Jump to the cleanup footer that touches teleportEffecters (so we still run Remove(thing))
            int endAnchorIndex = codes.FindIndex(c => c.LoadsField(teleportEffectersField));
            if (endAnchorIndex < 0)
            {
                foreach (var ci in codes) yield return ci;
                yield break;
            }

            // Back up to include the ldarg.0 before ldfld teleportEffecters
            int jumpTargetIndex = endAnchorIndex;
            while (jumpTargetIndex > 0 && codes[jumpTargetIndex].opcode == OpCodes.Nop) jumpTargetIndex--;

            // If we're sitting on the ldfld, step back one to the ldarg.0
            if (codes[jumpTargetIndex].LoadsField(teleportEffectersField) && jumpTargetIndex > 0)
                jumpTargetIndex--;

            var jumpLabel = generator.DefineLabel();
            codes[jumpTargetIndex].labels.Add(jumpLabel);

            // Insert after initial NOPs (keeps method-entry labels sane)
            int insertIndex = 0;
            while (insertIndex < codes.Count && codes[insertIndex].opcode == OpCodes.Nop) insertIndex++;

            for (int i = 0; i < insertIndex; i++)
                yield return codes[i];

            // if (TryRepositionIfPawn(thing, mapTarget, cellTarget)) goto footer;
            yield return new CodeInstruction(OpCodes.Ldarg_1); // Thing thing
            yield return new CodeInstruction(OpCodes.Ldarg_2); // Map mapTarget
            yield return new CodeInstruction(OpCodes.Ldarg_3); // IntVec3 cellTarget
            yield return new CodeInstruction(OpCodes.Call, tryRepositionMethod);
            yield return new CodeInstruction(OpCodes.Brtrue, jumpLabel);

            for (int i = insertIndex; i < codes.Count; i++)
                yield return codes[i];
        }
    }
}
