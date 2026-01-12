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
        // --- THROTTLING CACHE ---
        // Simple cache to prevent pawns from spamming checks (e.g. during combat micro)
        // Key: Pawn ID, Value: Last GameTick checked
        private static Dictionary<int, int> LastCheckTimes = new Dictionary<int, int>();
        private const int CHECK_INTERVAL_TICKS = 60; // Check at most once per second per pawn

        private struct TeleporterPathResult
        {
            public DoorTeleporter Teleporter;
            public float Cost;
            public PawnPath Path;
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
                    pawn.carryTracker.innerContainer.TryAdd(carriedThing);
                else
                    GenSpawn.Spawn(carriedThing, pawn.Position, pawn.Map);
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
            Map map = teleporter.Map;
            if (map == null) return false;

            foreach (IntVec3 c in GenAdj.CellsAdjacent8Way(teleporter))
            {
                if (!c.InBounds(map) || !c.Standable(map))
                    continue;

                bool blocked = c.GetThingList(map).Any(t =>
                    t.def.passability == Traversability.Impassable ||
                    t.def.passability == Traversability.PassThroughOnly
                );

                if (!blocked)
                    return true;
            }

            return false;
        }

        public static bool FindPathToTeleporter(Pawn pawn, LocalTargetInfo destination, PathEndMode peMode, out DoorTeleporter startTeleporter, out DoorTeleporter endTeleporter)
        {
            startTeleporter = null;
            endTeleporter = null;

            // 1. Basic Validation
            if (pawn.DestroyedOrNull() || pawn.Map == null || WorldComponent_DoorTeleporterManager.Instance.DoorTeleporters.Count < 2)
                return false;

            // 2. Throttling Check
            // If we checked recently, don't check again. Saves performance during high-APM moments or job spam.
            int currentTick = Find.TickManager.TicksGame;
            if (LastCheckTimes.TryGetValue(pawn.thingIDNumber, out int lastTick))
            {
                if (currentTick - lastTick < CHECK_INTERVAL_TICKS) return false;
            }
            LastCheckTimes[pawn.thingIDNumber] = currentTick;

            // Periodic cleanup of the dictionary (simple approach)
            if (currentTick % 2000 == 0 && LastCheckTimes.Count > 500) LastCheckTimes.Clear();

            // 3. Min Distance Optimization
            // If the trip is short, walking is always better/smoother.
            float straightLineDist = (pawn.Position - destination.Cell).LengthManhattan;
            if (straightLineDist < ModMain.Settings.MinTripDistance)
                return false;

            List<DoorTeleporter> mapTeleporters = WorldComponent_DoorTeleporterManager.Instance.DoorTeleporters.Where(t => t.Map == pawn.Map).ToList();
            if (mapTeleporters.Count < 2) return false;

            PathFinder pathFinder = pawn.Map.pathFinder;
            List<TeleporterPathResult> validStarts = new List<TeleporterPathResult>();
            List<TeleporterPathResult> validEnds = new List<TeleporterPathResult>();

            // Cache the settings for the loop
            float maxWalk = ModMain.Settings.MaxWalkToTeleporter;

            try
            {
                // --- A. Analyze Start Teleporters ---
                foreach (var t in mapTeleporters)
                {
                    if (t.DestroyedOrNull()) continue;

                    // Optimization: Radius Cap
                    // Don't calculate paths to doors that are too far away.
                    float dist = (pawn.Position - t.Position).LengthManhattan;
                    if (dist > maxWalk) continue;

                    if (pawn.Map.reachability.CanReach(pawn.Position, t.Position, PathEndMode.Touch, TraverseMode.PassDoors, Danger.Deadly))
                    {
                        PawnPath p = pathFinder.FindPathNow(pawn.Position, t.Position, pawn);
                        if (p != null && p != PawnPath.NotFound)
                        {
                            // FIXED COST CALCULATION:
                            // PathCost is roughly 13 per tile. Multiplying by 18 allows for snow/terrain overhead.
                            if (p.TotalCost <= maxWalk * 18)
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

                if (validStarts.Count == 0) return false;

                // --- B. Analyze End Teleporters ---
                foreach (var t in mapTeleporters)
                {
                    if (t.DestroyedOrNull() || !HasEmptyAdjacentSpot(t)) continue;

                    // Optimization: Radius Cap
                    float dist = (t.Position - destination.Cell).LengthManhattan;
                    if (dist > maxWalk) continue;

                    if (pawn.Map.reachability.CanReach(t.Position, destination, peMode, TraverseMode.PassDoors, Danger.Deadly))
                    {
                        PawnPath p = pathFinder.FindPathNow(t.Position, destination, pawn, null, peMode);
                        if (p != null && p != PawnPath.NotFound)
                        {
                            // FIXED COST CALCULATION
                            if (p.TotalCost <= maxWalk * 18)
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

                // --- C. Find Best Teleporter Combination ---
                float bestTeleporterCost = float.MaxValue;
                bool foundCandidate = false;

                foreach (var start in validStarts)
                {
                    foreach (var end in validEnds)
                    {
                        if (start.Teleporter == end.Teleporter) continue;

                        float totalCost = start.Cost + end.Cost + ModMain.PENALTY_FOR_USING_TELEPORTER;

                        if (totalCost < bestTeleporterCost)
                        {
                            bestTeleporterCost = totalCost;
                            startTeleporter = start.Teleporter;
                            endTeleporter = end.Teleporter;
                            foundCandidate = true;
                        }
                    }
                }

                if (!foundCandidate) return false;

                // --- D. LAZY BASELINE CHECK ---
                // "Is teleporting faster than flying?"
                // If the total cost to teleport is less than the straight-line distance to the goal,
                // it is MATHEMATICALLY IMPOSSIBLE for walking to be faster.
                // In this case, we return TRUE immediately and skip the expensive OriginalPath calculation.
                if (bestTeleporterCost < straightLineDist)
                {
                    return true;
                }

                // --- E. Fallback Baseline Check ---
                // If we are here, the teleporter path is decent, but maybe walking is still better 
                // because the teleporter path involves some walking. Now we MUST pay the cost to check the walk path.
                PawnPath originalPath = pathFinder.FindPathNow(pawn.Position, destination, pawn, null, peMode);
                if (originalPath == null || originalPath == PawnPath.NotFound)
                {
                    originalPath?.Dispose();
                    // If we can't walk there, but we found a teleporter path (validEnds checks reachability), take the teleporter!
                    return true;
                }

                float walkCost = originalPath.TotalCost;
                originalPath.Dispose();

                return bestTeleporterCost < walkCost;
            }
            finally
            {
                foreach (var res in validStarts) res.Path?.Dispose();
                foreach (var res in validEnds) res.Path?.Dispose();
            }
        }
    }
}