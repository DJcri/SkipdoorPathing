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
        private static bool IsPickUpAndHaulJob(Pawn pawn)
        {
            JobDef def = pawn?.CurJobDef;
            if (def == null) return false;

            // Don't ever treat the teleporter-use job as a PUAH job.
            if (def == VEFDefOf.VEF_UseDoorTeleporter)
                return false;

            // Common defNames used by Pick Up And Haul (and many forks).
            // We keep this intentionally narrow to avoid impacting other mods.
            string defName = def.defName;
            if (!defName.NullOrEmpty())
            {
                if (defName == "HaulToInventory" ||
                    defName == "UnloadYourHauledInventory" ||
                    defName == "OpportunisticHaul" ||
                    defName == "HaulToCellWithInventory" ||
                    defName == "HaulToContainerWithInventory")
                {
                    return true;
                }

                // Some forks prefix their defs.
                if (defName.IndexOf("PickUpAndHaul", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    defName.IndexOf("PUAH", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            // Fallback: detect by mod package id/name if present.
            string pkg = def.modContentPack?.PackageId;
            if (!pkg.NullOrEmpty())
            {
                // Covers typical package ids for PUaH and its maintained forks.
                if (pkg.IndexOf("pick up and haul", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    pkg.IndexOf("pickupandhaul", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    pkg.IndexOf("puah", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        // IMPORTANT: use ref so we can rewrite destination
        public static bool Prefix(Pawn_PathFollower __instance, ref LocalTargetInfo dest, ref PathEndMode peMode)
        {
            try
            {
                if (WorldComponent_DoorTeleporterManager.Instance.DoorTeleporters.Count == 0)
                    return true;

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
                    // IMPORTANT: Keep original behavior for all non-PUAH jobs.
                    // Only PUaH jobs use the "teleporter job" approach, because rewriting StartPath
                    // can confuse hauling toils (e.g. StartCarryThing triggering early).
                    if (!pawn.Drafted && IsPickUpAndHaulJob(pawn))
                    {
                        SkipdoorPathingUtil.UseDoorTeleporter(pawn, startTeleporter, endTeleporter);
                        return false; // Skip vanilla StartPath; the teleporter job will handle movement.
                    }

                    // Default behavior (unchanged): Save plan, then reroute the path to the ENTRY teleporter
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