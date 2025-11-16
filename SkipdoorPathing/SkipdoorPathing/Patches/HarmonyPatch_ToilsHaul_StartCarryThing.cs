using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using Verse.AI;

namespace SkipdoorPathing
{
    [HarmonyPatch(typeof(Toils_Haul), "StartCarryThing")]
    public static class HarmonyPatch_ToilsHaul_StartCarryThing
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            return instructions.AsEnumerable();
        }
    }
}
