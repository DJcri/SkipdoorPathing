using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;
using System.Linq;

namespace SkipdoorPathing
{
    [StaticConstructorOnStartup]
    public static class PatchInitializer
    {
        static PatchInitializer()
        {
            var harmony = new Harmony("DCSzar.SkipdoorPathing.DoorTeleporterOptimization");

            // 1. Find the target type.
            var type = AccessTools.TypeByName("VEF.Buildings.DoorTeleporter");
            if (type == null)
            {
                Log.Error("[DoorTeleporter_PatchInitializer] Failed to find target type 'VEF.Buildings.DoorTeleporter'.");
                return;
            }

            // 2. Find the target method.
            var originalMethod = AccessTools.Method(type, "Teleport", new[] { typeof(Thing), typeof(Map), typeof(IntVec3) });
            if (originalMethod == null)
            {
                Log.Error($"[DoorTeleporter_PatchInitializer] Failed to find 'Teleport' method in '{type.FullName}'.");
                return;
            }

            // 3. Apply the patch.
            var transpilerMethod = AccessTools.Method(typeof(DoorTeleporter_Teleport_Logic), nameof(DoorTeleporter_Teleport_Logic.Transpiler));
            harmony.Patch(originalMethod, transpiler: new HarmonyMethod(transpilerMethod));

            Log.Message($"[DoorTeleporter_PatchInitializer] Patched {originalMethod.FullDescription()}");
        }
    }

    public static class DoorTeleporter_Teleport_Logic
    {
        public static bool TryRepositionPawn(Pawn pawn, Map mapTarget, IntVec3 cellTarget)
        {
            // Check if it's a same-map move
            if (pawn.Map == mapTarget)
            {
                // 1. Perform the move
                pawn.Position = cellTarget;

                // 2. Handle side effects (pathing cache, job interruptions)
                // Notify_Teleported handles job cleanup (clearing reservations) and drawer updates
                pawn.Notify_Teleported();
                mapTarget.reachability.ClearCache();

                // 3. Reset the teleporting flag
                // We set it false here because we are skipping the code that normally sets it to false.
                pawn.teleporting = false;

                // 4. Reapply Saved Orders
                // CHANGE: Try to reapply Carry Job FIRST.
                // If the pawn was carrying someone (Rescue, Capture, or Drafted Carry), this restores that specific state.
                // If successful, we SKIP ReapplySavedMoveOrder to prevent it from overriding the carry job with a generic move that forces a drop.
                if (!PawnCarryManager.ReapplyCarryJob(pawn))
                {
                    PawnOrderManager.ReapplySavedMoveOrder(pawn);
                }

                return true; // Successfully repositioned
            }

            return false; // Inter-map move, fall back to original logic
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var codes = new List<CodeInstruction>(instructions);

            // --- FIELDS & METHODS ---
            var carryTrackerField = AccessTools.Field(typeof(Pawn), "carryTracker");
            var spawnMethod = AccessTools.Method(typeof(GenSpawn), nameof(GenSpawn.Spawn), new[] { typeof(Thing), typeof(IntVec3), typeof(Map), typeof(WipeMode) });
            var tryRepositionMethod = AccessTools.Method(typeof(DoorTeleporter_Teleport_Logic), nameof(TryRepositionPawn));

            // VEF Field: teleportEffecters
            var doorTeleporterType = AccessTools.TypeByName("VEF.Buildings.DoorTeleporter");
            var teleportEffectersField = AccessTools.Field(doorTeleporterType, "teleportEffecters");

            if (carryTrackerField == null || teleportEffectersField == null || spawnMethod == null)
            {
                Log.Error("[DoorTeleporter_Transpiler] Failed to find required fields/methods (carryTracker, teleportEffecters, or GenSpawn.Spawn).");
                yield return (CodeInstruction)instructions;
            }

            // --- STEP 1: Find the Start Anchor (The 'Drop Items' logic) ---
            int carryTrackerAccessIndex = codes.FindIndex(c => c.LoadsField(carryTrackerField));

            if (carryTrackerAccessIndex == -1)
            {
                Log.Error("[DoorTeleporter_Transpiler] Failed to find carryTracker access.");
                yield return (CodeInstruction)instructions;
            }

            // Backtrack to find the instruction loading the Pawn (e.g., Ldloc.s)
            int skipStartIndex = carryTrackerAccessIndex - 1;
            while (skipStartIndex > 0 && codes[skipStartIndex].opcode == OpCodes.Nop) skipStartIndex--;

            if (skipStartIndex < 0 || !codes[skipStartIndex].IsLdloc())
            {
                skipStartIndex = carryTrackerAccessIndex - 1;
            }

            // --- STEP 2: Find the End Anchor (The cleanup logic at the end) ---
            int spawnIndex = codes.FindIndex(c => c.Calls(spawnMethod));
            int endAnchorIndex = -1;

            if (spawnIndex != -1)
            {
                for (int i = spawnIndex; i < codes.Count; i++)
                {
                    if (codes[i].LoadsField(teleportEffectersField))
                    {
                        endAnchorIndex = i;
                        break;
                    }
                }
            }

            if (endAnchorIndex == -1)
            {
                Log.Error("[DoorTeleporter_Transpiler] Failed to find teleportEffecters access at end of method.");
                yield return (CodeInstruction)instructions;
            }

            // Backtrack to find the instruction loading 'this' (Ldarg.0) for the field access
            int jumpTargetIndex = endAnchorIndex - 1;
            while (jumpTargetIndex > spawnIndex && codes[jumpTargetIndex].opcode == OpCodes.Nop) jumpTargetIndex--;

            // --- STEP 3: Define Jump Label ---
            var jumpTargetInstruction = codes[jumpTargetIndex];
            var jumpLabel = generator.DefineLabel();
            jumpTargetInstruction.labels.Add(jumpLabel);

            // --- STEP 4: Emit Patch ---

            // A. Yield everything up to our hook point
            for (int i = 0; i < skipStartIndex; i++)
            {
                yield return codes[i];
            }

            // B. Inject the Check
            var pawnLoad = codes[skipStartIndex].Clone(); // Clone the Ldloc instruction

            yield return pawnLoad;                  // Load Pawn
            yield return new CodeInstruction(OpCodes.Ldarg_2); // Load Map
            yield return new CodeInstruction(OpCodes.Ldarg_3); // Load Cell
            yield return new CodeInstruction(OpCodes.Call, tryRepositionMethod); // Call Helper
            yield return new CodeInstruction(OpCodes.Brtrue, jumpLabel); // If true, JUMP TO END

            // C. Yield the original code (the block we might skip)
            codes[skipStartIndex].labels.Clear();

            for (int i = skipStartIndex; i < jumpTargetIndex; i++)
            {
                yield return codes[i];
            }

            // D. Yield the rest (Footer)
            for (int i = jumpTargetIndex; i < codes.Count; i++)
            {
                yield return codes[i];
            }
        }
    }
}