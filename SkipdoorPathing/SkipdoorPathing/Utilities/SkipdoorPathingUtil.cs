using System.Collections.Generic;
using System.Linq;
using RimWorld;
using SkipdoorPathing;
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
            pawn?.pather?.StopDead();
            JobPersistence.SavePawnState(pawn);
            bool shouldResumeJobNatively = false;
            Log.Message("SkipdoorPathing: Saving job state for pawn " + pawn.LabelShort + ".");
            Job teleportJob = JobMaker.MakeJob(VEFDefOf.VEF_UseDoorTeleporter, startTeleporter);
            teleportJob.globalTarget = endTeleporter;
            pawn?.jobs?.StartJob(teleportJob, JobCondition.InterruptForced, null, shouldResumeJobNatively, cancelBusyStances: true, null, null, fromQueue: false, canReturnCurJobToPool: true, true);
            foreach (Pawn item in pawn?.Map?.mapPawns?.AllPawnsSpawned)
            {
                if (item?.CurJobDef == JobDefOf.FollowClose && item?.CurJob?.targetA.Pawn == pawn)
                {
                    UseDoorTeleporter(item, startTeleporter, endTeleporter);
                }
            }
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
                if (endCandidate.DestroyedOrNull() || !pawn.Map.reachability.CanReach(endCandidate.Position, destination, peMode, TraverseMode.PassDoors, Danger.Deadly))
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