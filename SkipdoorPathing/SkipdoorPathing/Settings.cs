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

        // Runtime cache (not saved to file)
        public HashSet<string> CachedExclusions = new HashSet<string>();

        public override void ExposeData()
        {
            Scribe_Values.Look(ref CanAnimalsUseSkipdoors, "canAnimalsUseSkipdoors", false);
            Scribe_Values.Look(ref CanGuestsUseSkipdoors, "canGuestsUseSkipdoors", true);
            Scribe_Values.Look(ref CanSlavesUseSkipdoors, "canSlavesUseSkipdoors", true);
            Scribe_Values.Look(ref ExcludeWanderJobs, "excludeWanderJobs", true);
            Scribe_Values.Look(ref CustomExclusions, "customExclusions", "");

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