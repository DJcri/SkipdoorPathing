using HarmonyLib;
using UnityEngine;
using Verse;

namespace SkipdoorPathing
{
    public class ModMain : Mod
    {
        public static readonly float PENALTY_FOR_USING_TELEPORTER = 50f;

        // Removed static interval constant, handling this via Settings now if needed, 
        // but sticking to Logic Throttling in Utility.

        public static ModMain Instance;
        public static Harmony harmony;
        public static Settings Settings;

        public ModMain(ModContentPack content) : base(content)
        {
            harmony = new Harmony("DCSzar.SkipdoorPathing");
            harmony.PatchAll();
            Instance = this;

            Settings = GetSettings<Settings>();
            Settings.UpdateCache();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listingStandard = new Listing_Standard();
            listingStandard.Begin(inRect);

            // --- Animals ---
            listingStandard.CheckboxLabeled(
                "Allow colony animals to use Skipdoors",
                ref Settings.CanAnimalsUseSkipdoors,
                "If enabled, colony animals will consider Skipdoors for pathing. Default is off."
            );

            listingStandard.Gap();

            // --- Guests ---
            listingStandard.CheckboxLabeled(
                "Allow colony guests to use Skipdoors",
                ref Settings.CanGuestsUseSkipdoors,
                "If enabled, colony guests will consider Skipdoors for pathing. Default is on."
            );

            listingStandard.Gap();

            // --- Slaves ---
            listingStandard.CheckboxLabeled(
                "Allow colony slaves to use Skipdoors",
                ref Settings.CanSlavesUseSkipdoors,
                "If enabled, colony slaves will consider Skipdoors for pathing. Default is on."
            );

            listingStandard.Gap();

            // --- Wandering ---
            listingStandard.CheckboxLabeled(
                "Exclude Wandering Jobs",
                ref Settings.ExcludeWanderJobs,
                "If enabled, pawns will not use skipdoors for low-priority wandering behaviors."
            );

            listingStandard.GapLine();

            // --- OPTIMIZATIONS ---
            listingStandard.Label($"Minimum Trip Distance: {Settings.MinTripDistance}");
            Settings.MinTripDistance = listingStandard.Slider(Settings.MinTripDistance, 0f, 40f);
            listingStandard.Label("If the destination is closer than this, skipdoors are ignored.");

            listingStandard.Gap();

            listingStandard.Label($"Max Walk Distance to Door: {Settings.MaxWalkToTeleporter}");
            Settings.MaxWalkToTeleporter = listingStandard.Slider(Settings.MaxWalkToTeleporter, 200f, 400f);
            listingStandard.Label("Pawns won't consider walking further than this to reach a skipdoor.");

            listingStandard.GapLine();

            // --- Custom Exclusions ---
            listingStandard.Label("Custom JobDef Exclusions (Comma separated defNames):");
            string text = listingStandard.TextEntry(Settings.CustomExclusions);
            if (text != Settings.CustomExclusions)
            {
                Settings.CustomExclusions = text;
                Settings.UpdateCache();
            }
            listingStandard.Label("Example: LayEgg, Shear, RopeToPen");

            listingStandard.End();
            base.DoSettingsWindowContents(inRect);
        }

        public override string SettingsCategory()
        {
            return "Skipdoor Pathing";
        }
    }
}