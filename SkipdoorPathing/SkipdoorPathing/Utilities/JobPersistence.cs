using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using SkipdoorPathing;
using Verse;
using Verse.AI;

namespace SkipdoorPathing {
    public class SavedPawnState
    {
        public bool wasDrafted;
        public Job currentJob;
        public JobQueue jobQueue;
        public Thing carriedThing;
        public int carriedCount;
    }

    [StaticConstructorOnStartup]
    public static class JobPersistence
    {
        public static bool IsRestoringJob = false;

        private static Dictionary<int, SavedPawnState> savedStates = new Dictionary<int, SavedPawnState>();

        private static HashSet<int> pawnsToRestore = new HashSet<int>();

        private static Dictionary<int, int> teleportCooldowns = new Dictionary<int, int>();

        public static bool HasSavedState(Pawn pawn)
        {
            return savedStates.ContainsKey(pawn.thingIDNumber);
        }

        public static bool IsOnCooldown(Pawn pawn)
        {
            if (teleportCooldowns.TryGetValue(pawn.thingIDNumber, out var expiryTick))
            {
                if (Find.TickManager.TicksGame < expiryTick)
                {
                    return true;
                }
                teleportCooldowns.Remove(pawn.thingIDNumber);
                return false;
            }
            return false;
        }

        public static void QueueForDeferredRestore(Pawn pawn)
        {
            if (!pawn.DestroyedOrNull() && pawn.Spawned)
            {
                pawnsToRestore.Add(pawn.thingIDNumber);
            }
        }

        public static void DeferredRestoreTick(Pawn pawn)
        {
            if (pawnsToRestore.Contains(pawn.thingIDNumber) && TryRestorePawnState(pawn))
            {
                pawnsToRestore.Remove(pawn.thingIDNumber);
            }
        }

        public static void SavePawnState(Pawn pawn)
        {
            if (pawn.DestroyedOrNull() || pawn.jobs?.curJob == null)
            {
                return;
            }
            Job clonedJob = pawn.jobs.curJob.Clone();
            JobQueue clonedQueue = new JobQueue();
            foreach (QueuedJob item in pawn.jobs.jobQueue.Reverse())
            {
                clonedQueue.EnqueueFirst(item.job.Clone());
            }
            Thing carried = pawn.carryTracker.CarriedThing;
            int count = 0;
            if (carried != null)
            {
                count = carried.stackCount;
                if (count <= 0)
                {
                    Log.Warning("SkipdoorPathing: Pawn " + pawn.LabelShort + " was carrying " + carried.LabelCap + " with stackCount <= 0. Not saving carried item state.");
                    carried = null;
                    count = 0;
                }
            }
            SavedPawnState state = new SavedPawnState
            {
                wasDrafted = pawn.Drafted,
                currentJob = clonedJob,
                jobQueue = clonedQueue,
                carriedThing = carried,
                carriedCount = count
            };
            savedStates[pawn.thingIDNumber] = state;
        }

        public static bool TryRestorePawnState(Pawn pawn)
        {
            if (pawn.DestroyedOrNull() || !HasSavedState(pawn))
            {
                return false;
            }
            SavedPawnState state = savedStates[pawn.thingIDNumber];
            if (state.wasDrafted && !pawn.Drafted)
            {
                pawn.drafter.Drafted = true;
            }
            if (state.carriedThing != null)
            {
                if (pawn.carryTracker.CarriedThing != null && pawn.carryTracker.CarriedThing == state.carriedThing)
                {
                    Log.Message("SkipdoorPathing: Pawn " + pawn.LabelShort + " successfully kept " + state.carriedThing.LabelShort + " during teleport.");
                }
                else
                {
                    if (pawn.carryTracker.TryStartCarry(state.carriedThing, state.carriedCount) <= 0)
                    {
                        Log.Warning("SkipdoorPathing: Failed to restore carried item for " + pawn.LabelShort + ". Cannot restore job.");
                        savedStates.Remove(pawn.thingIDNumber);
                        return true;
                    }
                    Log.Message("SkipdoorPathing: Pawn " + pawn.LabelShort + " successfully restored carrying " + state.carriedThing.LabelShort + ".");
                }
            }
            if (state.currentJob == null)
            {
                savedStates.Remove(pawn.thingIDNumber);
                return true;
            }
            bool canReserve = true;
            if (state.currentJob.targetA.IsValid && !pawn.CanReserveAndReach(state.currentJob.targetA, PathEndMode.ClosestTouch, Danger.Deadly))
            {
                canReserve = false;
            }
            if (canReserve && state.currentJob.targetB.IsValid && !pawn.CanReserveAndReach(state.currentJob.targetB, PathEndMode.ClosestTouch, Danger.Deadly))
            {
                canReserve = false;
            }
            if (canReserve && (state.currentJob.def == JobDefOf.HaulToCell || state.currentJob.def == JobDefOf.HaulToContainer) && pawn.carryTracker.CarriedThing != null)
            {
                Job freshDropOffJob = JobMaker.MakeJob(JobDefOf.HaulToCell, pawn.carryTracker.CarriedThing, state.currentJob.targetB);
                if (state.carriedCount <= 0)
                {
                    Log.Error($"SkipdoorPathing: Tried to restore Haul job for {pawn.LabelShort} but state.carriedCount was {state.carriedCount}. Aborting job restore.");
                    savedStates.Remove(pawn.thingIDNumber);
                    return true;
                }
                freshDropOffJob.count = state.carriedCount;
                freshDropOffJob.haulMode = state.currentJob.haulMode;
                freshDropOffJob.locomotionUrgency = state.currentJob.locomotionUrgency;
                freshDropOffJob.targetQueueA = state.currentJob.targetQueueA;
                freshDropOffJob.targetQueueB = state.currentJob.targetQueueB;
                state.currentJob = freshDropOffJob;
                Log.Message("SkipdoorPathing Fix: Replaced stale Haul job with a fresh drop-off job for " + pawn.LabelShort + ".");
            }
            if (canReserve)
            {
                IsRestoringJob = true;
                try
                {
                    pawn.jobs.TryTakeOrderedJob(state.currentJob, JobTag.DraftedOrder);
                    if (state.jobQueue != null)
                    {
                        List<QueuedJob> jobList = state.jobQueue.ToList();
                        for (int i = jobList.Count - 1; i >= 0; i--)
                        {
                            pawn.jobs.jobQueue.EnqueueFirst(jobList[i].job);
                        }
                    }
                    teleportCooldowns[pawn.thingIDNumber] = Find.TickManager.TicksGame + ModMain.TELEPORTER_CHECK_INTERVAL * 2;
                }
                catch (Exception)
                {
                }
                finally
                {
                    IsRestoringJob = false;
                }
            }
            savedStates.Remove(pawn.thingIDNumber);
            return true;
        }
    }
}