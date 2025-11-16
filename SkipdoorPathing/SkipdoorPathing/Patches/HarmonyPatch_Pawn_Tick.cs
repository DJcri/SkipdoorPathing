using System;
using HarmonyLib;
using SkipdoorPathing;
using Verse;

namespace SkipdoorPathing
{
    [HarmonyPatch(typeof(Pawn), "Tick")]
    public class HarmonyPatch_Pawn_Tick
    {
        public static void Postfix(Pawn __instance)
        {
            try
            {
                JobPersistence.DeferredRestoreTick(__instance);
            }
            catch (Exception ex)
            {
                Log.Error("SkipdoorPathing failed during Pawn.Tick Postfix for deferred job restore on " + __instance.LabelShort + ": " + ex.Message);
            }
        }
    }
}
