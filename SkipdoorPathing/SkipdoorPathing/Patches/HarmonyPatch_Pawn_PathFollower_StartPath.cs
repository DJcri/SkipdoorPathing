using System;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using SkipdoorPathing;
using VEF;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace SkipdoorPathing
{
    [HarmonyPatch(typeof(Pawn_PathFollower), "StartPath")]
    public class HarmonyPatch_Pawn_PathFollower_StartPath
    {
        public static bool Prefix(Pawn_PathFollower __instance, LocalTargetInfo dest, PathEndMode peMode)
        {
            try
            {
                Pawn pawn = Traverse.Create((object)__instance).Field("pawn").GetValue<Pawn>();

                // Basic Checks
                if (pawn.DestroyedOrNull() || pawn.Map == null || !dest.IsValid)
                {
                    return true;
                }

                // --- FIX FOR LOOP START ---
                // If the pawn is already doing the Teleporter job, DO NOT interfere.
                // Otherwise, StartPath runs -> FindPathToTeleporter runs -> SaveCarryJob runs.
                // This overwrites the "Rescue" job with the "UseDoorTeleporter" job in the save slot.
                // When reapplied, the pawn loops the Teleporter job forever.
                if (pawn.CurJobDef == VEFDefOf.VEF_UseDoorTeleporter)
                {
                    return true;
                }
                // --- FIX FOR LOOP END ---

                if (!SkipExclusionUtility.ShouldSkipdoor(pawn))
                {
                    return true;
                }

                if (SkipdoorPathingUtil.FindPathToTeleporter(pawn, dest, peMode, out var startTeleporter, out var endTeleporter))
                {
                    // Save Carry Job (e.g., Rescue, Capture) BEFORE switching jobs
                    // Because of the check above, we know we are saving the ORIGINAL job (Rescue), not the Teleport job.
                    PawnCarryManager.SaveCarryJob(pawn);

                    // Save Drafted Move Order (e.g., Right-click Move)
                    PawnOrderManager.SaveDraftedMoveOrder(pawn);

                    SkipdoorPathingUtil.UseDoorTeleporter(pawn, startTeleporter, endTeleporter);
                    return false;
                }
            }
            catch (Exception ex)
            {
                // Log.Error("SkipdoorPathing failed during StartPath Prefix: " + ex.Message);
            }
            return true;
        }
    }
}