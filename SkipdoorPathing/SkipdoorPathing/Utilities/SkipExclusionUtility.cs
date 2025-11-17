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
        private static readonly HashSet<string> ProblematicJobDefs = new HashSet<string>
        {
            // QualityBuilder Mod: These jobs often cause NullReferenceExceptions when resumed.
            "ConstructFinishFrame", // The job that caused your specific error
            "Construct",
            "FinishFrame",
            
            // Other common complex jobs that might have modded persistence issues
            "DoBill", // Generic Bill jobs (used by many production mods)
            "ManThingToAllowHolding" // Complex hauling setups
        };

        public static bool ShouldSkipdoor(this Pawn pawn)
        {
            Log.Message($"SkipdoorPathing: Evaluating pawn {pawn.Name} for skipdoor eligibility.");
            if (JobPersistence.IsOnCooldown(pawn))
            {
                return false;
            }
            if (pawn.Faction != null &&
                !pawn.Faction.Equals(Faction.OfPlayer))
            {
                return false;
            }
            if (pawn.RaceProps.Animal && ModMain.Settings != null && !ModMain.Settings.CanAnimalsUseSkipdoors)
            {
                return false;
            }
            if (pawn.lord?.LordJob != null &&
                pawn.lord.LordJob is LordJob_Ritual)
            {
                return false;
            }
            if (pawn?.roping != null && (pawn.roping.IsRoped || pawn.roping.Ropees.Count > 0))
            {
                return false;
            }
            if (pawn?.jobs?.curJob != null &&
                (pawn.jobs.curJob.def.Equals(VEFDefOf.VEF_UseDoorTeleporter) ||
                pawn.jobs.curJob.def.Equals(JobDefOf.GotoWander) ||
                pawn.jobs.curJob.def.Equals(JobDefOf.RevenantWander) ||
                pawn.jobs.curJob.def.Equals(JobDefOf.Wait_Wander) ||
                (ProblematicJobDefs.Contains(pawn.jobs.curJob.def.defName) && ModMain.Settings.BlockComplexJobs)))
            {
                return false;
            }
            Log.Message($"SkipdoorPathing: Pawn {pawn.Name} passed all checks and is eligible for skipdoor usage.");
            return true;
        }
    }
}