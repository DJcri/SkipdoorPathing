using System;
using HarmonyLib;
using SkipdoorPathing;
using Verse;

namespace SkipdoorPathing
{
    [HarmonyPatch(typeof(Pawn), "SpawnSetup")]
    public class HarmonyPatch_Pawn_SpawnSetup
    {
        public static void Postfix(Pawn __instance)
        {
            try
            {
                if (JobPersistence.HasSavedState(__instance))
                {
                    JobPersistence.QueueForDeferredRestore(__instance);
                }
            }
            catch (Exception ex)
            {
                Log.Error("SkipdoorPathing failed during Pawn.SpawnSetup Postfix for " + __instance.LabelShort + ": " + ex.Message);
            }
        }
    }
}
