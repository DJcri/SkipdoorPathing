using HarmonyLib;
using Verse;

namespace SkipdoorPathing
{
    public class ModMain : Mod
    {
        // [ Mod Configs ]

        // Penalty cost to compare against original path cost. If the original path cost
        // is less than or equal to this value, teleporters won't be considered.
        public readonly static float PENALTY_FOR_USING_TELEPORTER = 50f;
        // Check frequency: 60 ticks = 1 check per second. This is a good balance between
        // responsiveness and CPU usage.
        public readonly static int TELEPORTER_CHECK_INTERVAL = 60;

        public static ModMain Instance;
        public static Harmony harmony;

        public ModMain(ModContentPack content) : base(content)
        {
            harmony = new Harmony("DCSzar.SkipdoorPathing");
            harmony.PatchAll();

            Instance = this;
        }
    }
}