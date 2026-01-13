using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using SkipdoorPathing;
using System;
using VEF;
using VEF.Buildings;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace SkipdoorPathing
{
    [HarmonyPatch(typeof(Pawn_PathFollower), "PatherTick")]
    public static class HarmonyPatch_Pawn_PathFollower_PatherTick
    {
        public static void Postfix(Pawn_PathFollower __instance)
        {
            var pawn = Traverse.Create(__instance).Field("pawn").GetValue<Pawn>();
            if (pawn?.Map == null) return;

            var mgr = pawn.Map.GetComponent<SkipdoorTransitManager>();
            mgr?.TryExecuteTeleportIfAtEntry(pawn);
        }
    }

    [HarmonyPatch(typeof(Pawn_PathFollower), "StartPath")]
    public class HarmonyPatch_Pawn_PathFollower_StartPath
    {
        // IMPORTANT: use ref so we can rewrite destination
        public static bool Prefix(Pawn_PathFollower __instance, ref LocalTargetInfo dest, ref PathEndMode peMode)
        {
            try
            {
                Pawn pawn = Traverse.Create(__instance).Field("pawn").GetValue<Pawn>();
                if (pawn.DestroyedOrNull() || pawn.Map == null || !dest.IsValid)
                    return true;

                if (!SkipExclusionUtility.ShouldSkipdoor(pawn))
                    return true;

                var mgr = pawn.Map.GetComponent<SkipdoorTransitManager>();

                // If we already have a plan, keep the pawn going to the entry while drafted lock is active.
                if (mgr != null && mgr.TryGetPlanDraftData(pawn, out var entryCell, out var lockUntil, out var finalDest, out var finalPeMode))
                {
                    int now = Find.TickManager.TicksGame;

                    if (pawn.Drafted && now < lockUntil)
                    {
                        dest = entryCell;
                        peMode = PathEndMode.OnCell;
                        return true;
                    }

                    // optional: if drafted and player changed destination, cancel plan
                    if (pawn.Drafted && dest.IsValid && finalDest.IsValid && dest.Cell != finalDest.Cell)
                    {
                        mgr.ClearPlan(pawn);
                        // fall through to normal planning
                    }
                    else
                    {
                        return true;
                    }
                }


                if (SkipdoorPathingUtil.FindPathToTeleporter(pawn, dest, peMode, out DoorTeleporter startTeleporter, out DoorTeleporter endTeleporter))
                {
                    // Save plan, then reroute the path to the ENTRY teleporter
                    mgr?.SetPlan(pawn, startTeleporter, endTeleporter, dest, peMode);

                    dest = startTeleporter.InteractionCell;
                    peMode = PathEndMode.OnCell;

                    return true; // allow vanilla StartPath with our rewritten destination
                }
            }
            catch (Exception)
            {
            }

            return true;
        }
    }
}