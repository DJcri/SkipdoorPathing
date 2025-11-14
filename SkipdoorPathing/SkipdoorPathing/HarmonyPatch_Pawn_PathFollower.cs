using HarmonyLib;
using VEF;
using VEF.Buildings;
using Verse;
using Verse.AI;

namespace SkipdoorPathing
{
    // We are patching PatherTick, which runs every tick and ensures the path is ready.
    [HarmonyPatch(typeof(Pawn_PathFollower), "PatherTick")]
    public class HarmonyPatch_Pawn_PathFollower
    {
        // Static counter to throttle the expensive pathfinding check.
        private static int tickCounter = 0;

        // Use a Prefix to potentially short-circuit the tick and start the teleport job
        public static void Prefix(Pawn_PathFollower __instance)
        {
            try
            {
                // --- THROTTLING MECHANISM ---
                // We only run the expensive teleporter check every CheckFrequency ticks.
                tickCounter++;
                if (tickCounter % ModMain.TELEPORTER_CHECK_INTERVAL != 0)
                {
                    return; // Skip the rest of the method most of the time
                }

                // Access the protected 'pawn' field using Harmony's Traverse class.
                Pawn pawn = Traverse.Create(__instance).Field("pawn").GetValue<Pawn>();

                // Basic checks
                if (pawn.DestroyedOrNull() || pawn.Drafted || pawn.Map == null)
                {
                    return;
                }

                // Only consider non-colonists if they are being controlled by the player (e.g. animals being moved)
                if (pawn.IsColonist || pawn.RaceProps.Animal || pawn.Faction.IsPlayer)
                {
                    // Do not interrupt if the pawn is already using a teleporter
                    if (pawn?.CurJob?.def == VEFDefOf.VEF_UseDoorTeleporter)
                    {
                        return;
                    }

                    // We check curPath here. If it's null, it means the pawn is not moving or the path is not ready yet.
                    if (__instance.curPath == null || !__instance.Destination.IsValid)
                    {
                        return;
                    }

                    // The core logic
                    DoorTeleporter startTeleporter;
                    DoorTeleporter endTeleporter;

                    // FindPathToTeleporter now returns bool and disposes of all path segments internally
                    bool foundTeleporterPath = SkipdoorPathingUtil.FindPathToTeleporter(pawn, out startTeleporter, out endTeleporter);

                    if (foundTeleporterPath)
                    {
                        // A better path was found! 

                        // Immediately interrupt the pawn's current activity and start the teleport job.
                        // The Job system safely disposes of the original pawn.pather.curPath.
                        SkipdoorPathingUtil.UseDoorTeleporter(pawn, startTeleporter, endTeleporter);
                    }
                }
            }
            catch (System.Exception ex)
            {
                // Log the error for debugging, but don't let it crash the job system
                Log.Error($"SkipdoorPathing failed during PatherTick Prefix: {ex.Message}");
            }
        }
    }
}