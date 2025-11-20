using HarmonyLib;
using RimWorld;
using SkipdoorPathing;
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
        public static void UseDoorTeleporter(Pawn pawn, DoorTeleporter startTeleporter, DoorTeleporter endTeleporter)
        {
            if (pawn.DestroyedOrNull() || startTeleporter.DestroyedOrNull() || endTeleporter.DestroyedOrNull())
            {
                return;
            }

            // Stop current movement
            pawn?.pather?.StopDead();

            bool shouldResumeJobNatively = true;

            Job teleportJob = JobMaker.MakeJob(VEFDefOf.VEF_UseDoorTeleporter, startTeleporter);
            teleportJob.globalTarget = endTeleporter;

            // --- WORKAROUND FOR SHARED TARGET INDEX CONFLICT ---
            // The VEF_UseDoorTeleporter job uses TargetIndex.A for the Portal.
            // If the pawn is carrying a person (Rescue job), the game sees that 
            // CarriedThing (Person) != Job.TargetA (Portal).
            // By default, StartJob drops any carried item that doesn't match the job's target
            // unless the JobDef explicitly allows opportunistic prefixes (which VEF might not).
            // To prevent this drop, we temporarily "hide" the carried item from the system 
            // while starting the job, then immediately put it back.

            Thing carriedThing = pawn.carryTracker?.CarriedThing;
            bool manuallyPreserved = false;

            if (carriedThing != null)
            {
                // Remove from container without spawning/dropping. 
                // It effectively vanishes from the pawn's "hands" for a split second so StartJob doesn't complain.
                pawn.carryTracker.innerContainer.Remove(carriedThing);
                manuallyPreserved = true;
            }

            // Start the job. StartJob sees empty hands, so it doesn't force a drop.
            pawn?.jobs?.StartJob(teleportJob, JobCondition.InterruptForced, null, shouldResumeJobNatively, cancelBusyStances: true, null, null, fromQueue: false, canReturnCurJobToPool: true, true);

            // Restore the carried item immediately
            if (manuallyPreserved && carriedThing != null)
            {
                // If pawn is still valid, put the item back in their hands.
                if (pawn.Spawned && !pawn.Dead && !pawn.Downed)
                {
                    // TryAdd handles updating the holdingOwner correctly
                    pawn.carryTracker.innerContainer.TryAdd(carriedThing);
                }
                else
                {
                    // Failsafe: if pawn died/despawned during StartJob (unlikely), spawn the item to prevent deletion.
                    GenSpawn.Spawn(carriedThing, pawn.Position, pawn.Map);
                }
            }

            // Recursion for followers
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
                    // Check for trees which are technically standable but block stopping
                    bool hasTree = c.GetThingList(teleporter.Map).Any(t => t.def.category == ThingCategory.Plant && (t.def.plant?.IsTree ?? false));

                    if (!hasTree)
                    {
                        return true;
                    }
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
            PathFinder pathFinder = pawn.Map.pathFinder;
            IntVec3 position = pawn.Position;
            PawnPath originalPath = pathFinder.FindPathNow(position, destination, pawn, null, peMode);
            if (originalPath == null)
            {
                return false;
            }
            float currentPathCost = originalPath.TotalCost;
            originalPath.Dispose();
            float bestTotalCost = currentPathCost;
            DoorTeleporter bestStart = null;
            DoorTeleporter bestEnd = null;
            PawnPath tempBestPathToStartSegment = null;
            List<DoorTeleporter> mapTeleporters = WorldComponent_DoorTeleporterManager.Instance.DoorTeleporters.Where((DoorTeleporter t) => t.Map == pawn.Map).ToList();
            if (mapTeleporters.Count < 2)
            {
                return false;
            }
            List<DoorTeleporter> potentialEndTeleporters = new List<DoorTeleporter>();
            foreach (DoorTeleporter teleporter in mapTeleporters)
            {
                if (pawn.Map.reachability.CanReach(teleporter.Position, destination, peMode, TraverseMode.PassDoors, Danger.Deadly))
                {
                    potentialEndTeleporters.Add(teleporter);
                }
            }
            if (potentialEndTeleporters.Count == 0)
            {
                return false;
            }
            foreach (DoorTeleporter endCandidate in mapTeleporters)
            {
                if (endCandidate.DestroyedOrNull() || !HasEmptyAdjacentSpot(endCandidate) || !pawn.Map.reachability.CanReach(endCandidate.Position, destination, peMode, TraverseMode.PassDoors, Danger.Deadly))
                {
                    continue;
                }
                PawnPath pathToDest = pawn.Map.pathFinder.FindPathNow(endCandidate.Position, destination, pawn, null, peMode);
                if (pathToDest == null)
                {
                    continue;
                }
                float costToDest = pathToDest.TotalCost;
                pathToDest.Dispose();
                DoorTeleporter bestStartCandidate = null;
                PawnPath bestPathToStart = null;
                float bestCostToStart = float.MaxValue;
                foreach (DoorTeleporter startCandidate in mapTeleporters)
                {
                    if (startCandidate.DestroyedOrNull() || startCandidate == endCandidate || !pawn.Map.reachability.CanReach(startCandidate.Position, destination, peMode, TraverseMode.PassDoors, Danger.Deadly))
                    {
                        continue;
                    }
                    PawnPath pathToStart = pawn.Map.pathFinder.FindPathNow(pawn.Position, startCandidate.Position, pawn);
                    if (pathToStart != null)
                    {
                        if (pathToStart.TotalCost < bestCostToStart)
                        {
                            bestPathToStart?.Dispose();
                            bestCostToStart = pathToStart.TotalCost;
                            bestPathToStart = pathToStart;
                            bestStartCandidate = startCandidate;
                        }
                        else
                        {
                            pathToStart.Dispose();
                        }
                    }
                }
                if (bestStartCandidate != null)
                {
                    float totalTeleportCost = bestCostToStart + costToDest + ModMain.PENALTY_FOR_USING_TELEPORTER;
                    if (totalTeleportCost < bestTotalCost)
                    {
                        tempBestPathToStartSegment?.Dispose();
                        bestTotalCost = totalTeleportCost;
                        tempBestPathToStartSegment = bestPathToStart;
                        bestStart = bestStartCandidate;
                        bestEnd = endCandidate;
                    }
                    else
                    {
                        bestPathToStart?.Dispose();
                    }
                }
            }
            if (tempBestPathToStartSegment != null)
            {
                tempBestPathToStartSegment.Dispose();
                startTeleporter = bestStart;
                endTeleporter = bestEnd;
                return true;
            }
            return false;
        }
    }
}