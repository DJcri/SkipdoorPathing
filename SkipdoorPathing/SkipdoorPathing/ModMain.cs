using HarmonyLib;
using Verse;

namespace SkipdoorPathing
{
    public class ModMain : Mod
    {
        public static readonly float PENALTY_FOR_USING_TELEPORTER = 50f;

        public static readonly int TELEPORTER_CHECK_INTERVAL = 60;

        public static ModMain Instance;

        public static Harmony harmony;

        public ModMain(ModContentPack content)
            : base(content)
        {
            Instance = this;
            harmony = new Harmony("DCSzar.SkipdoorPathing");
            harmony.PatchAll();
            Instance = this;
        }
    }
}