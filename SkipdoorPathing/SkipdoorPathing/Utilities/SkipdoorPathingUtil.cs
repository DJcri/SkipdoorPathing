using RimWorld;
using System.Collections.Generic;
using System.Linq;
using VEF;
using VEF.Buildings;
using Verse;
using Verse.AI;

namespace SkipdoorPathing
{
    [StaticConstructorOnStartup]
    public static class SkipdoorPathingUtil
    {
        // Simple struct to hold path results to avoid recalculating
        private struct TeleporterPathResult
        {
            public DoorTeleporter Teleporter;
            public float Cost;
            public PawnPath Path; // Ownership of this path must be managed carefully
        }

        public static void UseDoorTeleporter(Pawn pawn, DoorTeleporter startTeleporter, DoorTeleporter endTeleporter)
        {
            if (pawn.DestroyedOrNull() || startTeleporter.DestroyedOrNull() || endTeleporter.DestroyedOrNull())
            {
                return;
            }

            pawn?.pather?.StopDead();
            bool shouldResumeJobNatively = true;

            Job teleportJob = JobMaker.MakeJob(VEFDefOf.VEF_UseDoorTeleporter, startTeleporter);
            teleportJob.globalTarget = endTeleporter;

            // Workaround for dropped items
            Thing carriedThing = pawn.carryTracker?.CarriedThing;
            bool manuallyPreserved = false;

            if (carriedThing != null)
            {
                pawn.carryTracker.innerContainer.Remove(carriedThing);
                manuallyPreserved = true;
            }

            pawn?.jobs?.StartJob(teleportJob, JobCondition.InterruptForced, null, shouldResumeJobNatively, cancelBusyStances: true, null, null, fromQueue: false, canReturnCurJobToPool: true, true);

            if (manuallyPreserved && carriedThing != null)
            {
                if (pawn.Spawned && !pawn.Dead && !pawn.Downed)
                {
                    pawn.carryTracker.innerContainer.TryAdd(carriedThing);
                }
                else
                {
                    GenSpawn.Spawn(carriedThing, pawn.Position, pawn.Map);
                }
            }

            foreach (Pawn item in pawn?.Map?.mapPawns?.AllPawnsSpawned)
            {
                if (item?.CurJobDef == JobDefOf.FollowClose && item?.CurJob?.targetA.Pawn == pawn)
                {
                    UseDoorTeleporter(item, startTeleporter, endTeleporter);
                }
            }
        }

        private static bool HasEmptyAdjacentSpot(DoorTeleporter teleporter)
        {
            foreach (IntVec3 c in GenAdj.CellsAdjacent8Way(teleporter))
            {
                if (c.InBounds(teleporter.Map) && c.Standable(teleporter.Map))
                {
                    bool hasTree = c.GetThingList(teleporter.Map).Any(t => t.def.category == ThingCategory.Plant && (t.def.plant?.IsTree ?? false));
                    if (!hasTree) return true;
                }
            }
            return false;
        }

        public static bool FindPathToTeleporter(Pawn pawn, LocalTargetInfo destination, PathEndMode peMode, out DoorTeleporter startTeleporter, out DoorTeleporter endTeleporter)
        {
            startTeleporter = null;
            endTeleporter = null;

            if (pawn.DestroyedOrNull() || pawn.Map == null || WorldComponent_DoorTeleporterManager.Instance.DoorTeleporters.Count < 2)
            {
                return false;
            }

            // 1. Calculate Original Path Cost
            // We need this as a baseline. If using a teleporter isn't faster than this, we abort.
            PathFinder pathFinder = pawn.Map.pathFinder;
            PawnPath originalPath = pathFinder.FindPathNow(pawn.Position, destination, pawn, null, peMode);

            if (originalPath == null || originalPath == PawnPath.NotFound)
            {
                originalPath?.Dispose();
                return false;
            }

            float bestTotalCost = originalPath.TotalCost;
            originalPath.Dispose(); // We only needed the cost, not the nodes

            List<DoorTeleporter> mapTeleporters = WorldComponent_DoorTeleporterManager.Instance.DoorTeleporters.Where(t => t.Map == pawn.Map).ToList();
            if (mapTeleporters.Count < 2) return false;

            // 2. Identify Candidates
            // Optimization: Split the loop. 
            // Finding a path from Pawn -> StartTeleporter is independent of which EndTeleporter we choose.
            // Finding a path from EndTeleporter -> Destination is independent of which StartTeleporter we choose.

            List<TeleporterPathResult> validStarts = new List<TeleporterPathResult>();
            List<TeleporterPathResult> validEnds = new List<TeleporterPathResult>();

            try
            {
                // --- A. Analyze Start Teleporters ---
                foreach (var t in mapTeleporters)
                {
                    if (t.DestroyedOrNull()) continue;

                    // Heuristic check: If distance alone is greater than best cost, skip pathfinding
                    float dist = (pawn.Position - t.Position).LengthManhattan;
                    if (dist > bestTotalCost) continue;

                    if (pawn.Map.reachability.CanReach(pawn.Position, t.Position, PathEndMode.Touch, TraverseMode.PassDoors, Danger.Deadly))
                    {
                        PawnPath p = pathFinder.FindPathNow(pawn.Position, t.Position, pawn);
                        if (p != null && p != PawnPath.NotFound)
                        {
                            // Optimization: If path to teleporter is already worse than walking, discard
                            if (p.TotalCost + ModMain.PENALTY_FOR_USING_TELEPORTER < bestTotalCost)
                            {
                                validStarts.Add(new TeleporterPathResult { Teleporter = t, Cost = p.TotalCost, Path = p });
                            }
                            else
                            {
                                p.Dispose();
                            }
                        }
                    }
                }

                // If no valid starts, abort early
                if (validStarts.Count == 0) return false;

                // --- B. Analyze End Teleporters ---
                foreach (var t in mapTeleporters)
                {
                    if (t.DestroyedOrNull() || !HasEmptyAdjacentSpot(t)) continue;

                    // Heuristic check
                    float dist = (t.Position - destination.Cell).LengthManhattan;
                    if (dist > bestTotalCost) continue;

                    if (pawn.Map.reachability.CanReach(t.Position, destination, peMode, TraverseMode.PassDoors, Danger.Deadly))
                    {
                        PawnPath p = pathFinder.FindPathNow(t.Position, destination, pawn, null, peMode);
                        if (p != null && p != PawnPath.NotFound)
                        {
                            if (p.TotalCost < bestTotalCost)
                            {
                                validEnds.Add(new TeleporterPathResult { Teleporter = t, Cost = p.TotalCost, Path = p });
                            }
                            else
                            {
                                p.Dispose();
                            }
                        }
                    }
                }

                if (validEnds.Count == 0) return false;

                // --- C. Find Best Combination ---
                // Loop through valid starts and ends to find the best combo.
                // This is fast because we are just adding floats, not pathfinding.

                bool foundBetterPath = false;

                foreach (var start in validStarts)
                {
                    foreach (var end in validEnds)
                    {
                        // Cannot teleport to the same door
                        if (start.Teleporter == end.Teleporter) continue;

                        float totalCost = start.Cost + end.Cost + ModMain.PENALTY_FOR_USING_TELEPORTER;

                        if (totalCost < bestTotalCost)
                        {
                            bestTotalCost = totalCost;
                            startTeleporter = start.Teleporter;
                            endTeleporter = end.Teleporter;
                            foundBetterPath = true;
                        }
                    }
                }

                return foundBetterPath;
            }
            finally
            {
                // CLEANUP: We must dispose of all paths we generated
                foreach (var res in validStarts) res.Path?.Dispose();
                foreach (var res in validEnds) res.Path?.Dispose();
            }
        }
    }
}