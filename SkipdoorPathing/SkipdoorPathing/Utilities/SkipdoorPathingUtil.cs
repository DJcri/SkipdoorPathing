using RimWorld;
using System.Collections.Generic;
using VEF;
using VEF.Buildings;
using Verse;
using Verse.AI;

namespace SkipdoorPathing
{
    [StaticConstructorOnStartup]
    public static class SkipdoorPathingUtil
    {
        // --- THROTTLING + DECISION CACHE ---
        // Prevent expensive teleporter evaluation spam (combat micro, drafted zig-zag, etc.)
        // Key: Pawn thingIDNumber
        private struct PawnDecisionCache
        {
            public int LastTick;
            public IntVec3 LastDest;
            public PathEndMode LastPeMode;
            public int LastTeleporterCount;

            public bool LastResult;
            public DoorTeleporter LastStart;
            public DoorTeleporter LastEnd;
        }

        private static readonly Dictionary<int, PawnDecisionCache> DecisionCache = new Dictionary<int, PawnDecisionCache>(512);

        private const int CHECK_INTERVAL_TICKS = 60; // at most once per second per pawn
        private const int MAX_START_CANDIDATES = 8; // shortlist cap (keeps behavior similar while reducing path calls)
        private const int MAX_END_CANDIDATES = 8;

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

                // Avoid LINQ/allocs: scan things directly
                List<Thing> things = c.GetThingList(map);
                bool blocked = false;
                for (int i = 0; i < things.Count; i++)
                {
                    Thing t = things[i];
                    if (t?.def == null) continue;
                    var pass = t.def.passability;
                    if (pass == Traversability.Impassable || pass == Traversability.PassThroughOnly)
                    {
                        blocked = true;
                        break;
                    }
                }

                if (!blocked)
                    return true;
            }

            return false;
        }

        public static bool FindPathToTeleporter(
            Pawn pawn,
            LocalTargetInfo destination,
            PathEndMode peMode,
            out DoorTeleporter startTeleporter,
            out DoorTeleporter endTeleporter)
        {
            startTeleporter = null;
            endTeleporter = null;

            // 1. Basic Validation
            if (pawn.DestroyedOrNull() || pawn.Map == null)
                return false;

            // Global list must contain at least 2 to matter
            var globalList = WorldComponent_DoorTeleporterManager.Instance?.DoorTeleporters;
            if (globalList == null || globalList.Count < 2)
                return false;

            // 2. Min Distance Optimization
            IntVec3 destCell = destination.Cell;
            IntVec3 pawnPos = pawn.Position;

            int straightLineDist = (pawnPos - destCell).LengthManhattan;
            if (straightLineDist < ModMain.Settings.MinTripDistance)
                return false;

            Map map = pawn.Map;

            // 3. Throttling + Decision Cache (uses mapTeleporterCount, computed below)
            int currentTick = Find.TickManager.TicksGame;
            int pawnId = pawn.thingIDNumber;

            // Periodic cleanup (cheap)
            if (currentTick % 2000 == 0 && DecisionCache.Count > 1500)
                DecisionCache.Clear();

            // Cache settings for loops
            int maxWalk = (int)ModMain.Settings.MaxWalkToTeleporter;
            int maxWalkCost = maxWalk * 18; // same heuristic as before

            // --- Candidate shortlists (single pass over HashSet) ---
            int mapTeleporterCount = 0;
            var startDists = new List<(DoorTeleporter t, int dist)>(MAX_START_CANDIDATES);
            var endDists = new List<(DoorTeleporter t, int dist)>(MAX_END_CANDIDATES);

            foreach (var t in globalList)
            {
                if (t == null || t.DestroyedOrNull() || t.Map != map) continue;
                mapTeleporterCount++;

                int dStart = (pawnPos - t.Position).LengthManhattan;
                if (dStart <= maxWalk)
                {
                    AddCandidateSortedLimited(startDists, t, dStart, MAX_START_CANDIDATES);
                }

                int dEnd = (t.Position - destCell).LengthManhattan;
                if (dEnd <= maxWalk)
                {
                    // IMPORTANT micro-optimization:
                    // Only pay HasEmptyAdjacentSpot() if this candidate might actually enter the top-N list.
                    // If endDists isn't full yet, or it's better than the current worst, then check adjacency.
                    if (endDists.Count < MAX_END_CANDIDATES || dEnd < endDists[endDists.Count - 1].dist)
                    {
                        if (HasEmptyAdjacentSpot(t))
                        {
                            AddCandidateSortedLimited(endDists, t, dEnd, MAX_END_CANDIDATES);
                        }
                    }
                }
            }

            // Need at least two teleporters on this map to matter
            if (mapTeleporterCount < 2)
                return false;

            // Cache check now that we know teleporter count on this map
            if (DecisionCache.TryGetValue(pawnId, out PawnDecisionCache cached))
            {
                if (currentTick - cached.LastTick < CHECK_INTERVAL_TICKS &&
                    cached.LastPeMode == peMode &&
                    cached.LastTeleporterCount == mapTeleporterCount &&
                    cached.LastDest == destCell)
                {
                    if (cached.LastResult &&
                        !cached.LastStart.DestroyedOrNull() &&
                        !cached.LastEnd.DestroyedOrNull())
                    {
                        startTeleporter = cached.LastStart;
                        endTeleporter = cached.LastEnd;
                        return true;
                    }
                    return false;
                }
            }

            // If no candidates within maxWalk, bail
            if (startDists.Count == 0 || endDists.Count == 0)
                return false;

            // Sort by distance and cap to shortlist size
            startDists.Sort((a, b) => a.Item2.CompareTo(b.Item2));
            endDists.Sort((a, b) => a.Item2.CompareTo(b.Item2));

            if (startDists.Count > MAX_START_CANDIDATES)
                startDists.RemoveRange(MAX_START_CANDIDATES, startDists.Count - MAX_START_CANDIDATES);

            if (endDists.Count > MAX_END_CANDIDATES)
                endDists.RemoveRange(MAX_END_CANDIDATES, endDists.Count - MAX_END_CANDIDATES);

            // --- A. Compute real start costs (shortlist only) ---
            var startCosts = new List<(DoorTeleporter, float)>(startDists.Count);
            var reach = map.reachability;
            var pathFinder = map.pathFinder;

            for (int i = 0; i < startDists.Count; i++)
            {
                DoorTeleporter t = startDists[i].Item1;
                if (t.DestroyedOrNull()) continue;

                if (!reach.CanReach(pawnPos, t.Position, PathEndMode.Touch, TraverseMode.PassDoors, Danger.Deadly))
                    continue;

                PawnPath p = pathFinder.FindPathNow(pawnPos, t.Position, pawn);
                if (p == null || p == PawnPath.NotFound)
                {
                    p?.Dispose();
                    continue;
                }

                float cost = p.TotalCost;
                p.Dispose();

                if (cost <= maxWalkCost)
                    startCosts.Add((t, cost));
            }

            if (startCosts.Count == 0)
                return false;

            // --- B. Compute real end costs (shortlist only) ---
            var endCosts = new List<(DoorTeleporter, float)>(endDists.Count);

            for (int i = 0; i < endDists.Count; i++)
            {
                DoorTeleporter t = endDists[i].Item1;
                if (t.DestroyedOrNull()) continue;

                if (!reach.CanReach(t.Position, destination, peMode, TraverseMode.PassDoors, Danger.Deadly))
                    continue;

                PawnPath p = pathFinder.FindPathNow(t.Position, destination, pawn, null, peMode);
                if (p == null || p == PawnPath.NotFound)
                {
                    p?.Dispose();
                    continue;
                }

                float cost = p.TotalCost;
                p.Dispose();

                if (cost <= maxWalkCost)
                    endCosts.Add((t, cost));
            }

            if (endCosts.Count == 0)
                return false;

            // --- C. Find best pair ---
            float bestTeleporterCost = float.MaxValue;
            DoorTeleporter bestStart = null;
            DoorTeleporter bestEnd = null;

            for (int i = 0; i < startCosts.Count; i++)
            {
                DoorTeleporter sT = startCosts[i].Item1;
                float sCost = startCosts[i].Item2;

                for (int j = 0; j < endCosts.Count; j++)
                {
                    DoorTeleporter eT = endCosts[j].Item1;
                    if (sT == eT) continue;

                    float total = sCost + endCosts[j].Item2 + ModMain.PENALTY_FOR_USING_TELEPORTER;
                    if (total < bestTeleporterCost)
                    {
                        bestTeleporterCost = total;
                        bestStart = sT;
                        bestEnd = eT;
                    }
                }
            }

            bool result = false;

            if (bestStart != null && bestEnd != null)
            {
                // Preserve original "lazy baseline" logic to keep behavior consistent.
                if (bestTeleporterCost < straightLineDist)
                {
                    startTeleporter = bestStart;
                    endTeleporter = bestEnd;
                    result = true;
                }
                else
                {
                    // Expensive baseline walk check (only when needed)
                    PawnPath originalPath = pathFinder.FindPathNow(pawnPos, destination, pawn, null, peMode);
                    if (originalPath == null || originalPath == PawnPath.NotFound)
                    {
                        originalPath?.Dispose();
                        startTeleporter = bestStart;
                        endTeleporter = bestEnd;
                        result = true;
                    }
                    else
                    {
                        float walkCost = originalPath.TotalCost;
                        originalPath.Dispose();

                        if (bestTeleporterCost < walkCost)
                        {
                            startTeleporter = bestStart;
                            endTeleporter = bestEnd;
                            result = true;
                        }
                    }
                }
            }

            // 5. Update decision cache
            DecisionCache[pawnId] = new PawnDecisionCache
            {
                LastTick = currentTick,
                LastDest = destCell,
                LastPeMode = peMode,
                LastTeleporterCount = mapTeleporterCount,
                LastResult = result,
                LastStart = startTeleporter,
                LastEnd = endTeleporter
            };

            return result;
        }

        // Keeps list sorted by dist (ascending) and limited to maxCount.
        // If the new item is worse than the current worst, it is ignored.
        private static void AddCandidateSortedLimited(
            List<(DoorTeleporter t, int dist)> list,
            DoorTeleporter t,
            int dist,
            int maxCount)
        {
            int count = list.Count;

            // If list is full and this candidate is not better than the worst, bail fast.
            if (count >= maxCount && dist >= list[count - 1].dist)
                return;

            // Find insertion index (linear scan; list is tiny: <= 24)
            int insertIndex = count;
            for (int i = 0; i < count; i++)
            {
                if (dist < list[i].dist)
                {
                    insertIndex = i;
                    break;
                }
            }

            if (count < maxCount)
            {
                list.Insert(insertIndex, (t, dist));
                return;
            }

            // list is full and dist is better than worst:
            // insert then drop the last (worst)
            list.Insert(insertIndex, (t, dist));
            list.RemoveAt(maxCount);
        }
    }
}
