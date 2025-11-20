using System.Collections.Generic;
using RimWorld;
using VEF;
using Verse;
using Verse.AI;

namespace SkipdoorPathing
{
    [StaticConstructorOnStartup]
    public static class PawnCarryManager
    {
        private class SavedCarryJob
        {
            public JobDef def;
            public LocalTargetInfo targetA;
            public LocalTargetInfo targetB;
            public LocalTargetInfo targetC;
            public int count;
            public Thing carriedThing;
            public bool playerForced;
            public bool ignoreForbidden;
            public bool ignoreDesignations;
        }

        private static Dictionary<Pawn, SavedCarryJob> savedCarryOrders = new Dictionary<Pawn, SavedCarryJob>();

        public static bool SaveCarryJob(Pawn pawn)
        {
            if (pawn == null || !pawn.Spawned || pawn.Dead || pawn.Downed) return false;

            if (pawn.carryTracker == null || pawn.carryTracker.CarriedThing == null || !(pawn.carryTracker.CarriedThing is Pawn)) return false;

            Job curJob = pawn.CurJob;

            if (curJob == null || curJob.def == VEFDefOf.VEF_UseDoorTeleporter) return false;

            SavedCarryJob saveData = new SavedCarryJob
            {
                def = curJob.def,
                targetA = curJob.targetA,
                targetB = curJob.targetB,
                targetC = curJob.targetC,
                count = curJob.count,
                carriedThing = pawn.carryTracker.CarriedThing,
                playerForced = curJob.playerForced,
                ignoreForbidden = curJob.ignoreForbidden,
                ignoreDesignations = curJob.ignoreDesignations
            };

            if (savedCarryOrders.ContainsKey(pawn))
            {
                savedCarryOrders[pawn] = saveData;
            }
            else
            {
                savedCarryOrders.Add(pawn, saveData);
                Log.Message($"Saved carry order for {pawn.LabelShort}: {saveData.def.defName}");
            }

            return true;
        }

        public static bool ReapplyCarryJob(Pawn pawn)
        {
            if (pawn == null || !pawn.Spawned || pawn.Dead || pawn.Downed) return false;

            if (savedCarryOrders.TryGetValue(pawn, out SavedCarryJob saved))
            {
                savedCarryOrders.Remove(pawn);

                Job newJob = JobMaker.MakeJob(saved.def, saved.targetA, saved.targetB, saved.targetC);
                newJob.count = saved.count;
                newJob.playerForced = saved.playerForced;
                newJob.ignoreForbidden = saved.ignoreForbidden;
                newJob.ignoreDesignations = saved.ignoreDesignations;

                if (saved.targetA.Thing == saved.carriedThing) newJob.targetA = pawn.carryTracker.CarriedThing;
                if (saved.targetB.Thing == saved.carriedThing) newJob.targetB = pawn.carryTracker.CarriedThing;
                if (saved.targetC.Thing == saved.carriedThing) newJob.targetC = pawn.carryTracker.CarriedThing;

                // --- DRAFTED MOVE WORKAROUND (Specific to Carrying Pawns) ---
                // Transitioning from "CarryDownedPawnDrafted" -> "GoTo" is valid.
                // Transitioning from "HaulItem" -> "GoTo" usually forces a drop in Vanilla.
                if (saved.def == JobDefOf.Goto && saved.carriedThing is Pawn)
                {
                    // 1. Force the pawn into the "Carry Drafted" state.
                    Job intermediateJob = JobMaker.MakeJob(JobDefOf.CarryDownedPawnDrafted, saved.carriedThing);
                    intermediateJob.count = 1;
                    pawn.jobs.TryTakeOrderedJob(intermediateJob, JobTag.DraftedOrder);

                    // 2. Now apply the Move order. 
                    pawn.jobs.jobQueue.EnqueueLast(newJob, JobTag.DraftedOrder);
                    return true;
                }

                // CRITICAL: Ensure we are still holding the exact same object.
                if (pawn.carryTracker != null && pawn.carryTracker.CarriedThing == saved.carriedThing)
                {
                    // --- STANDARD RESTORATION (HaulToTransporter, Rescue, Capture) ---
                    Thing carriedThing = pawn.carryTracker.CarriedThing;

                    try
                    {
                        Job intermediateJob = JobMaker.MakeJob(JobDefOf.CarryDownedPawnDrafted, saved.carriedThing);
                        intermediateJob.count = 1;
                        pawn.jobs.TryTakeOrderedJob(intermediateJob, JobTag.DraftedOrder);
                        pawn.jobs.jobQueue.EnqueueLast(newJob, JobTag.MiscWork);
                    }
                    catch
                    {
                        // Log.Message("Something went wrong reapplying carried pawn");
                    }

                    return true;
                }
            }

            return false;
        }
    }
}