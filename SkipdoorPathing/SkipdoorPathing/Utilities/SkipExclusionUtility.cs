using RimWorld;
using System;
using System.Collections.Generic;
using VEF;
using Verse;
using Verse.AI;

namespace SkipdoorPathing
{
    public static class SkipExclusionUtility
    {
        // Behavioral job exclusions (non-logistical)
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
            "LoadVehicle",
            // "CarryItemToVehicle",
            // "PrepareCaravan_GatheringVehicle",
            "RopeAnimalToVehicle",
            // "CarryPawnToVehicle",
            // "LoadUpgradeMaterials"
        };

        public static bool ShouldSkipdoor(this Pawn pawn)
        {
            // 1. Faction Check
            if (pawn.Faction == null || pawn.Faction != Faction.OfPlayer)
                return false;

            // 1.5 Guest / Prisoner / Slave Check
            if (pawn.GuestStatus != null)
            {
                if (pawn.GuestStatus == GuestStatus.Guest && !ModMain.Settings.CanGuestsUseSkipdoors)
                    return false;

                if (pawn.GuestStatus == GuestStatus.Slave && !ModMain.Settings.CanSlavesUseSkipdoors)
                    return false;

                if (pawn.GuestStatus == GuestStatus.Prisoner)
                    return false;
            }

            // 2. Animal Settings Check
            if (pawn.RaceProps.Animal &&
                ModMain.Settings != null &&
                !ModMain.Settings.CanAnimalsUseSkipdoors)
            {
                return false;
            }

            // 3. Roping Check (fast hard stop)
            if (pawn.roping != null &&
                (pawn.roping.IsRoped || pawn.roping.Ropees.Count > 0))
            {
                return false;
            }

            // 4. Job Analysis
            Job job = pawn.jobs?.curJob;
            if (job != null)
            {
                JobDef def = job.def;

                // A. Skipdoor recursion / logic safety
                if (def == VEFDefOf.VEF_UseDoorTeleporter)
                    return false;

                // B. Wander jobs (user setting)
                if (ModMain.Settings.ExcludeWanderJobs)
                {
                    if (def == JobDefOf.GotoWander ||
                        def == JobDefOf.Wait_Wander ||
                        def == JobDefOf.RevenantWander)
                    {
                        return false;
                    }
                }

                // C. Ritual jobs
                if (JobIsRitual(pawn) || JobDriverIsRitual(job))
                {
                    return false; // Skipdoors are disabled for rituals
                }

                // D. Behavioral job exclusions
                if (DefaultExcludedJobs.Contains(def.defName))
                    return false;

                // E. User custom exclusions
                if (ModMain.Settings.CachedExclusions.Contains(def.defName))
                    return false;
            }

            return true;
        }

        // -------------------------
        // Helper Methods
        // -------------------------

        private static bool JobIsRitual(Pawn pawn)
        {
            if (pawn?.lord?.LordJob == null) return false;

            // Check if the LordJob is a ritual
            var lordJob = pawn.lord.LordJob;

            // RimWorld vanilla ritual jobs include:
            // LordJob_Ritual, LordJob_WeddingCeremony, LordJob_BestowingCeremony
            string name = lordJob.GetType().Name;

            return name.Contains("Ritual") ||
                   name.Contains("Wedding") ||
                   name.Contains("Bestowing");
        }

        private static bool JobDriverIsRitual(Job job)
        {
            if (job?.def?.driverClass == null) return false;

            string driverName = job.def.driverClass.Name;
            return driverName.Contains("Ritual") || driverName.Contains("Ceremony");
        }
    }
}
