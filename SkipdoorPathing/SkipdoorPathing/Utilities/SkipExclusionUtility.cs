using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using VEF;
using Verse;
using Verse.AI.Group;

namespace SkipdoorPathing

{
    public static class SkipExclusionUtility
    {
        public static bool ShouldSkipdoor(this Pawn pawn)
        {
            if (pawn.Faction != null &&
                !pawn.Faction.Equals(Faction.OfPlayer))
            {
                return false;
            }
            if (pawn.RaceProps.Animal && ModMain.Settings != null && !ModMain.Settings.CanAnimalsUseSkipdoors)
            {
                return false;
            }
            if (pawn?.roping != null && (pawn.roping.IsRoped || pawn.roping.Ropees.Count > 0))
            {
                return false;
            }
            if (pawn?.jobs?.curJob != null &&
                (pawn.jobs.curJob.def.Equals(VEFDefOf.VEF_UseDoorTeleporter) ||
                (pawn.jobs.curJob.def.Equals(JobDefOf.HaulToTransporter) && pawn.jobs.curJob.targetA.Thing is Pawn carriedThing) ||
                pawn.jobs.curJob.def.Equals(JobDefOf.GotoWander) ||
                pawn.jobs.curJob.def.Equals(JobDefOf.RevenantWander) ||
                pawn.jobs.curJob.def.Equals(JobDefOf.Wait_Wander)))
            {
                return false;
            }

            return true;
        }
    }
}