using System.Collections.Generic;
using Verse;

namespace SkipdoorPathing
{
    public class Settings : ModSettings
    {
        public bool CanAnimalsUseSkipdoors = false;
        public bool CanGuestsUseSkipdoors = true;
        public bool CanSlavesUseSkipdoors = true;
        public bool ExcludeWanderJobs = true;
        public string CustomExclusions = "";

        // --- NEW OPTIMIZATION SETTINGS ---
        public float MinTripDistance = 20f;      // If trip is shorter than this, don't bother checking teleporters
        public float MaxWalkToTeleporter = 300f;  // If teleporter is further than this from pawn/dest, ignore it

        // How many promising teleporters to fully path-evaluate on each side (entry + exit).
        // Lower values are faster; higher values are smarter in weird layouts.
        public int MaxCandidates = 3;

        // Runtime cache (not saved to file)
        public HashSet<string> CachedExclusions = new HashSet<string>();

        public override void ExposeData()
        {
            Scribe_Values.Look(ref CanAnimalsUseSkipdoors, "canAnimalsUseSkipdoors", false);
            Scribe_Values.Look(ref CanGuestsUseSkipdoors, "canGuestsUseSkipdoors", true);
            Scribe_Values.Look(ref CanSlavesUseSkipdoors, "canSlavesUseSkipdoors", true);
            Scribe_Values.Look(ref ExcludeWanderJobs, "excludeWanderJobs", true);
            Scribe_Values.Look(ref CustomExclusions, "customExclusions", "");

            // Save new settings
            Scribe_Values.Look(ref MinTripDistance, "minTripDistance", 20f);
            Scribe_Values.Look(ref MaxWalkToTeleporter, "maxWalkToTeleporter", 300f);
            Scribe_Values.Look(ref MaxCandidates, "maxCandidates", 3);

            base.ExposeData();
        }

        public void UpdateCache()
        {
            CachedExclusions.Clear();
            if (!CustomExclusions.NullOrEmpty())
            {
                string[] array = CustomExclusions.Split(',');
                foreach (string text in array)
                {
                    string cleanText = text.Trim();
                    if (!string.IsNullOrEmpty(cleanText))
                    {
                        CachedExclusions.Add(cleanText);
                    }
                }
            }
        }
    }
}