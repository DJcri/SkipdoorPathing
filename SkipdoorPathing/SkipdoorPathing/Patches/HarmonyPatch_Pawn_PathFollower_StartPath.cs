
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
                if (pawn.Map == null)
                {
                    return true;
                }
                if (JobPersistence.IsOnCooldown(pawn) || pawn.DestroyedOrNull() || pawn.Map == null || pawn.Faction == null || (!pawn.IsColonistPlayerControlled && !pawn.IsPlayerControlledCaravanMember()) || (pawn.jobs.curJob != null && pawn.jobs.curJob.def == VEFDefOf.VEF_UseDoorTeleporter) || !dest.IsValid)
                {
                    return true;
                }
                Lord lord = pawn.GetLord();
                if (lord != null && (lord.LordJob is LordJob_Joinable_MarriageCeremony || lord.LordJob is LordJob_Joinable_Gathering || lord.LordJob is LordJob_BestowingCeremony || lord.LordJob is LordJob_Ritual || lord.LordJob is LordJob_ExitMapBest || lord.LordJob is LordJob_ExitMapNear || lord.LordJob is LordJob_TravelAndExit || lord.LordJob is LordJob_WaitForDurationThenExit || lord.LordJob is LordJob_FormAndSendCaravan))
                {
                    return true;
                }
                Pawn_RopeTracker roping = pawn.roping;
                if ((roping != null && roping.IsRopingOthers) || pawn.roping.Ropees.Count > 0)
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