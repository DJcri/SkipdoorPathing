
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
                if (pawn.DestroyedOrNull() || pawn.Map == null || !dest.IsValid)
                {
                    return true;
                }
                if (!SkipExclusionUtility.ShouldSkipdoor(pawn))
                {
                    return true;
                }
                if (SkipdoorPathingUtil.FindPathToTeleporter(pawn, dest, peMode, out var startTeleporter, out var endTeleporter))
                {
                    SkipdoorPathingUtil.UseDoorTeleporter(pawn, startTeleporter, endTeleporter);
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.Error("SkipdoorPathing failed during StartPath Prefix: " + ex.Message);
            }
            return true;
        }
    }
}