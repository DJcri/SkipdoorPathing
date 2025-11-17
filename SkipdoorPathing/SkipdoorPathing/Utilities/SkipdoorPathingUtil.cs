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
            PathEndMode peMode2 = peMode;
            PawnPath originalPath = pathFinder.FindPathNow(position, destination, pawn, null, peMode2);
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
            List<DoorTeleporter> mapTeleporters = WorldComponent_DoorTeleporterManager.Instance.DoorTeleporters
                            .Where(t => t.Map == pawn.Map)
                            .ToList();

            if (mapTeleporters.Count < 2)
            {
                return false; // Not enough teleporters on this map to form a pair
            }

            var validTeleporterPairs = mapTeleporters
                .GroupBy(t => t.Label)
                .Where(g => g.Count() == 2) // Only proceed with groups that have exactly two teleporters
                .Select(g => new {
                    Start = g.First(),
                    End = g.Last()
                })
                .SelectMany(pair => new[]
                {
                    new { Start = pair.Start, End = pair.End }, // Start -> End
                    new { Start = pair.End, End = pair.Start }  // End -> Start (for bidirectional travel)
                });

            foreach (var pair in validTeleporterPairs)
            {
                DoorTeleporter teleporterToPathTo = pair.Start;
                DoorTeleporter teleporter = pair.End;
                if (teleporterToPathTo.DestroyedOrNull() || teleporter.DestroyedOrNull())
                {
                    continue;
                }
                PawnPath pathToStart = pawn.Map.pathFinder.FindPathNow(pawn.Position, teleporterToPathTo.Position, pawn);
                if (pathToStart == null || pathToStart.TotalCost >= bestTotalCost)
                {
                    pathToStart?.Dispose();
                    continue;
                }
                PathFinder pathFinder2 = pawn.Map.pathFinder;
                IntVec3 position2 = teleporter.Position;
                peMode2 = peMode;
                PawnPath pathToDest = pathFinder2.FindPathNow(position2, destination, pawn, null, peMode2);
                if (pathToDest == null)
                {
                    pathToStart?.Dispose();
                    continue;
                }
                float totalTeleportCost = pathToStart.TotalCost + pathToDest.TotalCost + ModMain.PENALTY_FOR_USING_TELEPORTER;
                if (totalTeleportCost < bestTotalCost)
                {
                    tempBestPathToStartSegment?.Dispose();
                    bestTotalCost = totalTeleportCost;
                    tempBestPathToStartSegment = pathToStart;
                    bestStart = teleporterToPathTo;
                    bestEnd = teleporter;
                }
                else
                {
                    pathToStart?.Dispose();
                }
                pathToDest?.Dispose();
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