using System;
using HarmonyLib;
using RimWorld.Planet;
using SkipdoorPathing;
using VEF;
using Verse;
using Verse.AI;

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
                if (JobPersistence.IsOnCooldown(pawn))
                {
                    return true;
                }
                if (pawn.DestroyedOrNull() || pawn.Map == null || pawn.Faction == null)
                {
                    return true;
                }
                if (!pawn.IsColonistPlayerControlled && !pawn.IsPlayerControlledCaravanMember())
                {
                    return true;
                }
                if (pawn.jobs.curJob != null && pawn.jobs.curJob.def == VEFDefOf.VEF_UseDoorTeleporter)
                {
                    return true;
                }
                if (!dest.IsValid)
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