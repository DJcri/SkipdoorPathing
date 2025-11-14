using RimWorld;
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

            // Create a job for the pawn to use the teleporter
            Job teleportJob = JobMaker.MakeJob(VEFDefOf.VEF_UseDoorTeleporter, startTeleporter);
            teleportJob.globalTarget = endTeleporter;
            Log.Message($"[SkipdoorPathing] Pawn {pawn.LabelShort} is using teleporter {startTeleporter.Label} to go to {endTeleporter.Label}.");

            // CRITICAL FIX: Removed explicit path disposal. The StartJob with JobCondition.InterruptForced 
            // is the correct, safe way for the game engine to dispose of the old path and prevent leaks/corruption.
            // pawn.pather.curPath.Dispose(); // <-- REMOVED

            pawn?.jobs?.StartJob(teleportJob, JobCondition.InterruptForced, null, resumeCurJobAfterwards: true, cancelBusyStances: true, null, null, fromQueue: false, keepCarryingThingOverride: true, canReturnCurJobToPool: true);

            // Handle followers
            foreach (Pawn item in pawn?.Map?.mapPawns?.AllPawnsSpawned)
            {
                if (item?.CurJobDef == JobDefOf.FollowClose && item?.CurJob?.targetA.Pawn == pawn)
                {
                    Job job2 = JobMaker.MakeJob(VEFDefOf.VEF_UseDoorTeleporter, startTeleporter);
                    job2.globalTarget = endTeleporter;
                    item.jobs.TryTakeOrderedJob(job2, JobTag.Misc);
                }
            }
        }

        public static bool CanPathToDestFromTeleporter(Pawn pawn, DoorTeleporter teleporter, IntVec3 dest)
        {
            // Check if the pawn can path from the teleporter to the destination
            // Return true if it can, false otherwise
            return (bool)(pawn?.Map?.reachability?.CanReach(teleporter.Position, dest, PathEndMode.OnCell, TraverseParms.For(pawn, Danger.Deadly, TraverseMode.ByPawn, alwaysUseAvoidGrid: false)));
        }

        public static DoorTeleporter FindLinkedTeleporter(DoorTeleporter teleporter)
        {
            // Find the linked teleporter for the given teleporter
            var doorTeleporters = WorldComponent_DoorTeleporterManager.Instance.DoorTeleporters.Except(teleporter);
            return doorTeleporters?.FirstOrDefault(dt => dt.Label.Equals(teleporter.Label)) ?? null;
        }

        // Main method to find a teleporter to use for pathing. Returns bool to indicate success.
        public static bool FindPathToTeleporter(Pawn pawn, out DoorTeleporter startTeleporter, out DoorTeleporter endTeleporter)
        {
            startTeleporter = null;
            endTeleporter = null;
            Map map = pawn.Map;

            // [ NULL CHECKS ]
            PawnPath currentPatherPath = pawn?.pather?.curPath;
            LocalTargetInfo destinationInfo = pawn?.pather?.Destination ?? null;

            if (currentPatherPath == null || destinationInfo == null || !destinationInfo.IsValid)
            {
                return false;
            }

            IntVec3 dest = destinationInfo.Cell;
            float originalCost = currentPatherPath.TotalCost;

            if (originalCost <= ModMain.PENALTY_FOR_USING_TELEPORTER)
            {
                return false;
            }

            var doorTeleporters = WorldComponent_DoorTeleporterManager.Instance.DoorTeleporters;
            if (doorTeleporters.NullOrEmpty())
            {
                return false;
            }

            PawnPath tempBestPathToStartSegment = null; // Temporarily holds the best path segment found so far
            DoorTeleporter bestStart = null;
            DoorTeleporter bestEnd = null;
            float bestTotalCost = originalCost; // Start by assuming the original path is best

            // [ MAIN LOGIC ]
            foreach (var teleporter in doorTeleporters.ToList())
            {
                DoorTeleporter teleporterToPathTo = FindLinkedTeleporter(teleporter);
                if (teleporterToPathTo == null || teleporterToPathTo == teleporter)
                {
                    continue;
                }

                // 3. (Expensive) Find path from PAWN -> START
                PawnPath pathToStart = map.pathFinder.FindPathNow(pawn.Position, teleporterToPathTo.Position, pawn);

                // 4. (Cheap) "Fail fast" check
                if (pathToStart == null || !pathToStart.Found || pathToStart.TotalCost >= bestTotalCost)
                {
                    pathToStart?.Dispose(); // Dispose unused path segment
                    continue;
                }

                // 5. (Expensive) Find path from END -> DESTINATION
                PawnPath pathToDest = map.pathFinder.FindPathNow(teleporter.Position, dest, pawn);

                // 6. (Cheap) "Fail fast" check
                if (pathToDest == null || !pathToDest.Found)
                {
                    pathToStart?.Dispose(); // Dispose unused path segment
                    pathToDest?.Dispose(); // Dispose unused path segment
                    continue;
                }

                // 7. The FINAL, correct comparison
                float totalTeleportCost = pathToStart.TotalCost + pathToDest.TotalCost + ModMain.PENALTY_FOR_USING_TELEPORTER;

                if (totalTeleportCost < bestTotalCost)
                {
                    // Dispose of the old 'best' path segment before overwriting the reference
                    tempBestPathToStartSegment?.Dispose();

                    // We found a new best path! Update our records.
                    bestTotalCost = totalTeleportCost;
                    tempBestPathToStartSegment = pathToStart; // Temporarily store the best path segment found so far
                    bestStart = teleporterToPathTo;
                    bestEnd = teleporter;
                }
                else
                {
                    // This path segment wasn't better, dispose of it
                    pathToStart?.Dispose();
                }
                pathToDest?.Dispose(); // Dispose of the path segment from END to DEST
            }

            // 8. After checking all pairs, if we found a better path, clean up the final segment and return true.
            if (tempBestPathToStartSegment != null)
            {
                // CRITICAL FIX: The path segment is not needed for movement (a new job is started), 
                // so we must dispose of it here to prevent a leak!
                tempBestPathToStartSegment.Dispose();

                startTeleporter = bestStart;
                endTeleporter = bestEnd;

                Log.Message($"[SkipdoorPathing] Pawn {pawn.LabelShort} found better teleporter path via {startTeleporter.Label} to {endTeleporter.Label} with total cost {bestTotalCost} (original cost: {originalCost}).");
                return true; // Indicate success
            }

            return false; // No teleport path was better
        }
    }
}