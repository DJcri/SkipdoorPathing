using HarmonyLib;
using UnityEngine;
using Verse;

namespace SkipdoorPathing
{
    public class ModMain : Mod
    {
        public static readonly float PENALTY_FOR_USING_TELEPORTER = 50f;

        public static readonly int TELEPORTER_CHECK_INTERVAL = 60;

        public static ModMain Instance;

        public static Harmony harmony;

        public static Settings Settings;

        public ModMain(ModContentPack content)
            : base(content)
        {
            harmony = new Harmony("DCSzar.SkipdoorPathing");
            harmony.PatchAll();
            Instance = this;

            Settings = GetSettings<Settings>();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listingStandard = new Listing_Standard();
            listingStandard.Begin(inRect);

            // Settings Toggle
            listingStandard.CheckboxLabeled(
                "Allow player-owned animals to use Skipdoors",
                ref Settings.CanAnimalsUseSkipdoors,
                "If enabled, player-factioned animals will consider Skipdoors for pathing. Default is off."
            );

            listingStandard.CheckboxLabeled(
                "Block complex jobs from using Skipdoors (Recommended for large modpacks)", 
                ref Settings.BlockComplexJobs,
                "If enabled, pawns currently running complex work jobs (like Constructing or DoBill) are prevented from teleporting to avoid red errors caused by mod conflicts (e.g., QualityBuilder). Disable this only if you are confident in your mod list."
            );

            listingStandard.End();
            base.DoSettingsWindowContents(inRect);
        }

        public override string SettingsCategory()
        {
            return "Skipdoor Pathing";
        }
    }
}