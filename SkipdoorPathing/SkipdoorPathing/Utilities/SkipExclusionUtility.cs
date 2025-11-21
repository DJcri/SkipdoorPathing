using RimWorld;
using System.Collections.Generic;
using VEF;
using Verse;
using Verse.AI;

namespace SkipdoorPathing
{
    public static class SkipExclusionUtility
    {
        // Hardcoded list of major job types that should generally NOT use teleporters.
        // This includes animal production, sensitive interactions, and ceremony logic.
        // Users no longer need to know these specific defNames.
        private static readonly HashSet<string> DefaultExcludedJobs = new HashSet<string>
        {
            // --- Animal Actions ---
            "LayEgg",
            "Mate",
            "Nuzzle",
            "PredatorHunt", // Predators teleporting can be unfair or buggy
            
            // --- Animal Handling ---
            // "Shear",
            // "Milk",
            // "Tame",
            // "Train",
            // "RopeToPen",
            "Unrope",
            // "ReleaseAnimalToWild",
            
            // --- Social / Ceremonies ---
            // Prevents pawns from teleporting short distances during weddings/speeches
            "MarryAdjacentPawn",
            // "SpectateCeremony",
            "GiveSpeech",
            "BestowingCeremony",
        };

        public static bool ShouldSkipdoor(this Pawn pawn)
        {
            // 1. Faction Check
            if (pawn.Faction == null || (pawn.Faction != null &&
                !pawn.Faction.Equals(Faction.OfPlayer)))
            {
                return false;
            }

            // 1.5 Guest Check
            if (pawn.HomeFaction != null && !pawn.HomeFaction.Equals(Faction.OfPlayer) ||
                (pawn.GuestStatus != null &&
                pawn.GuestStatus.Equals(GuestStatus.Guest) && !ModMain.Settings.CanGuestsUseSkipdoors &&
                pawn.GuestStatus.Equals(GuestStatus.Slave) && !ModMain.Settings.CanSlavesUseSkipdoors &&
                pawn.GuestStatus.Equals(GuestStatus.Prisoner)))
            {
                return false;
            }

            // 2. Animal Settings Check
            if (pawn.RaceProps.Animal && ModMain.Settings != null && !ModMain.Settings.CanAnimalsUseSkipdoors)
            {
                return false;
            }

            // 3. Roping Check (Redundant with job check but faster/safer)
            if (pawn.roping != null && (pawn.roping.IsRoped || pawn.roping.Ropees.Count > 0))
            {
                return false;
            }

            // 5. Job Analysis
            if (pawn.jobs?.curJob != null)
            {
                JobDef def = pawn.jobs.curJob.def;
                string defName = def.defName;

                // A. Critical Mechanics (Infinite Loop / Logic Break Prevention)
                if (def == VEFDefOf.VEF_UseDoorTeleporter) return false;
                if (def == JobDefOf.HaulToTransporter && pawn.jobs.curJob.targetA.Thing is Pawn) return false;

                // B. Wandering Checks
                if (ModMain.Settings.ExcludeWanderJobs)
                {
                    if (pawn?.jobs?.curJob != null &&
                        pawn.jobs.curJob.def.Equals(VEFDefOf.VEF_UseDoorTeleporter) ||
                        pawn.jobs.curJob.def.Equals(JobDefOf.HaulToTransporter) ||
                        pawn.jobs.curJob.def.Equals(JobDefOf.GotoWander) ||
                        pawn.jobs.curJob.def.Equals(JobDefOf.RevenantWander) ||
                        pawn.jobs.curJob.def.Equals(JobDefOf.Wait_Wander))
                    {
                        return false;
                    }
                }

                // C. Major Job Type List (Default Exclusions)
                if (DefaultExcludedJobs.Contains(defName))
                {
                    return false;
                }

                // D. User Custom Exclusions (From Settings)
                if (ModMain.Settings.CachedExclusions.Contains(defName))
                {
                    return false;
                }
            }

            return true;
        }
    }
}