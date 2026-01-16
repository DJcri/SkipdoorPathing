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
                if (WorldComponent_DoorTeleporterManager.Instance.DoorTeleporters.Count == 0)
                    return true;

                Pawn pawn = Traverse.Create(__instance).Field("pawn").GetValue<Pawn>();
                if (pawn.DestroyedOrNull() || pawn.Map == null || !dest.IsValid)
                    return true;

                // Never interfere with the actual teleporter-use job; it manages its own movement.
                // Without this, we can recurse (StartPath -> teleport job -> StartPath ...).
                if (pawn.CurJobDef == VEFDefOf.VEF_UseDoorTeleporter)
                    return true;

                if (!SkipExclusionUtility.ShouldSkipdoor(pawn))
                    return true;

                var mgr = pawn.Map.GetComponent<SkipdoorTransitManager>();

                // If we already have a plan, keep the pawn going to the entry while drafted lock is active.
                if (mgr != null && mgr.TryGetPlanDraftData(pawn, out var entryCell, out var lockUntil, out var finalDest, out var finalPeMode))
                {
                    int now = Find.TickManager.TicksGame;

                    // Plans are only meaningful for drafted / player-forced movement.
                    // If a non-drafted pawn still has a plan (eg. after a job transition), clear it so we don't
                    // accidentally suppress future routing decisions.
                    if (!pawn.Drafted)
                    {
                        mgr.ClearPlan(pawn);
                    }

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
                        // If drafted and destination hasn't changed, keep existing plan.
                        // If not drafted, we just cleared the plan above and should allow new planning.
                        if (pawn.Drafted)
                            return true;
                    }
                }


                if (SkipdoorPathingUtil.FindPathToTeleporter(pawn, dest, peMode, out DoorTeleporter startTeleporter, out DoorTeleporter endTeleporter))
                {
                    // IMPORTANT COMPAT NOTE:
                    // Rewriting StartPath(dest) to the teleporter entry can break job-driver toils that assume
                    // "arrived" means arrived at the original target (eg. hauling: GotoThing -> StartCarryThing).
                    // In those cases, the next toil may run while the pawn is only at the skipdoor entry, and
                    // can "teleport" the target item into the pawn's hands.
                    //
                    // To avoid that, for *non-drafted* pawns we perform an explicit teleporter-use job that
                    // resumes the original job afterwards (VEF does this safely). For drafted movement, we
                    // keep the plan-based reroute so player goto remains smooth.

                    if (!pawn.Drafted)
                    {
                        SkipdoorPathingUtil.UseDoorTeleporter(pawn, startTeleporter, endTeleporter);
                        return false; // Skip the original StartPath for this tick; job will resume after teleport.
                    }

                    // Drafted / player-forced: Save plan, then reroute the path to the ENTRY teleporter.
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