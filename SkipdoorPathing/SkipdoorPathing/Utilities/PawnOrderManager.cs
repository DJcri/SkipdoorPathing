using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using System.Linq;

/// <summary>
/// Manages the saving and reapplying of a drafted pawn's immediate movement orders.
/// This allows a pawn to temporarily execute another action (like using an ability or taking cover)
/// and then quickly resume their previous movement destination.
/// </summary>
namespace SkipdoorPathing
{
    [StaticConstructorOnStartup]
    public static class PawnOrderManager
    {
        // Dictionary to store the saved destination cell (IntVec3) for each pawn.
        private static Dictionary<Pawn, IntVec3> savedMoveOrders = new Dictionary<Pawn, IntVec3>();

        // Temporary flag to indicate that the saved move order should be reapplied 
        // when the pawn's *current* job completes.
        private static Dictionary<Pawn, bool> shouldReapplyMoveAfterJobCompletion = new Dictionary<Pawn, bool>();

        /// <summary>
        /// Attempts to save the current movement destination for a drafted pawn.
        /// This only works if the pawn's current job is a standard "GoTo" (drafted movement).
        /// </summary>
        /// <param name="pawn">The pawn whose order should be saved.</param>
        /// <returns>True if an order was successfully saved, false otherwise.</returns>
        public static bool SaveDraftedMoveOrder(Pawn pawn)
        {
            // 1. Basic checks
            if (pawn == null || !pawn.Spawned || pawn.Dead || pawn.Downed)
            {
                return false;
            }

            if (!pawn.Drafted)
            {
                // This is a common warning, but keep silent for performance
                return false;
            }

            // 2. Check current job
            Job curJob = pawn.CurJob;
            if (curJob == null || curJob.def != JobDefOf.Goto || !curJob.playerForced)
            {
                // We only care about explicit drafted movement jobs.
                return false;
            }

            // 3. Save the target
            IntVec3 targetCell = curJob.targetA.Cell;
            savedMoveOrders[pawn] = targetCell;

            Log.Message($"Saved drafted move order for {pawn.NameShortColored} to cell: {targetCell}");
            return true;
        }

        /// <summary>
        /// Checks if a drafted move order is currently saved for the pawn.
        /// </summary>
        public static bool IsMoveOrderSaved(Pawn pawn)
        {
            return savedMoveOrders.ContainsKey(pawn);
        }

        /// <summary>
        /// Clears the saved move order. Used when the move order is transferred to the carry job.
        /// </summary>
        public static void ClearSavedMoveOrder(Pawn pawn)
        {
            if (savedMoveOrders.ContainsKey(pawn))
            {
                Log.Message($"Cleared saved move order for {pawn.NameShortColored}.");
                savedMoveOrders.Remove(pawn);
            }
        }

        /// <summary>
        /// Sets a flag to reapply the saved move order when the current job completes.
        /// </summary>
        public static void SetShouldReapplyMoveOrderOnJobCompletion(Pawn pawn, bool value)
        {
            if (value)
            {
                shouldReapplyMoveAfterJobCompletion[pawn] = true;
            }
            else
            {
                shouldReapplyMoveAfterJobCompletion.Remove(pawn);
            }
            Log.Message($"Set reapply move order on job completion for {pawn.NameShortColored}: {value}");
        }

        /// <summary>
        /// Checks if the flag is set to reapply move order after job completion.
        /// </summary>
        public static bool ShouldReapplyMoveOrderOnJobCompletion(Pawn pawn)
        {
            return shouldReapplyMoveAfterJobCompletion.ContainsKey(pawn);
        }

        /// <summary>
        /// Attempts to reapply the saved movement destination for a drafted pawn.
        /// </summary>
        /// <param name="pawn">The pawn to receive the saved order.</param>
        /// <returns>True if an order was successfully reapplied, false otherwise.</returns>
        public static bool ReapplySavedMoveOrder(Pawn pawn)
        {
            // 1. Basic checks
            if (pawn == null || !pawn.Spawned || pawn.Dead || pawn.Downed || !pawn.Drafted)
            {
                return false;
            }

            // 2. Check if an order is saved
            if (savedMoveOrders.TryGetValue(pawn, out IntVec3 targetCell))
            {
                // 3. Create and assign the new Goto job
                Job newJob = JobMaker.MakeJob(JobDefOf.Goto, targetCell);
                newJob.playerForced = true; // Crucial: Re-set as a forced order

                // Set the job as a forced, non-queueable action, replacing the current job.
                pawn.jobs.TryTakeOrderedJob(newJob, JobTag.Misc);

                // 4. Clean up the saved order
                savedMoveOrders.Remove(pawn);

                // Also clear the completion flag if it was set (though usually handled by the patch)
                shouldReapplyMoveAfterJobCompletion.Remove(pawn);

                Log.Message($"Reapplied saved move order for {pawn.NameShortColored} to cell: {targetCell}");
                return true;
            }
            else
            {
                // Log.Message($"No saved move order found for {pawn.NameShortColored}.");
                return false;
            }
        }
    }
}