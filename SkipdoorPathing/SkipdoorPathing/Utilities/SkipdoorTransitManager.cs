using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using VEF.Buildings;
using Verse;
using Verse.AI;

namespace SkipdoorPathing
{
    public class SkipdoorTransitManager : MapComponent
    {
        // --- plan cleanup knobs ---
        private const int PLAN_TTL_TICKS = 900;              // 15 seconds at 60 TPS
        private const int TRANSIT_TTL_TICKS = 120;           // 2 seconds (transit is 15 ticks; this is a safety net)
        private const int UNREACHABLE_RECHECK_INTERVAL = 60; // once per second

        // Avoid per-tick allocations in MapComponentTick
        private readonly List<Pawn> tmpPawns = new List<Pawn>(128);
        private List<SavedPlan> savedPlans;

        // --- compat hooks ---
        // Track very recent teleports so other logic (including compat patches) can avoid
        // expensive or unintuitive re-evaluations on the exact teleport tick.
        private readonly Dictionary<Pawn, int> justTeleportedTick = new Dictionary<Pawn, int>();

        /// <summary>
        /// True if the pawn currently has an active skipdoor plan and is in the transit countdown.
        /// Useful for compat patches (e.g. suppressing "unload" during transit).
        /// </summary>
        public bool IsInTransit(Pawn pawn)
        {
            return TryGetPlanInternal(pawn, out Plan plan) && plan.inTransit;
        }

        /// <summary>
        /// True if the pawn completed a skipdoor teleport within the last <paramref name="withinTicks"/>.
        /// </summary>
        public bool WasJustTeleported(Pawn pawn, int withinTicks = 2)
        {
            if (pawn == null) return false;
            if (!justTeleportedTick.TryGetValue(pawn, out int t)) return false;
            int now = Find.TickManager.TicksGame;
            return now - t >= 0 && now - t <= withinTicks;
        }

        public override void ExposeData()
        {
            base.ExposeData();

            // Save: convert dictionary to a scribed list
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                savedPlans = new List<SavedPlan>(plans.Count);
                foreach (var kv in plans)
                {
                    if (kv.Key != null)
                        savedPlans.Add(new SavedPlan(kv.Key, kv.Value));
                }
            }

            Scribe_Collections.Look(ref savedPlans, "skipdoorPlans", LookMode.Deep);

            // Load: rebuild dictionary, skip invalid refs
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                plans.Clear();

                if (savedPlans != null)
                {
                    foreach (var sp in savedPlans)
                    {
                        if (sp?.pawn == null) continue;

                        // Make sure it belongs to this map and is still valid
                        if (sp.pawn.DestroyedOrNull() || sp.pawn.Map != map || !sp.pawn.Spawned || sp.pawn.Dead)
                            continue;

                        if (sp.entry.DestroyedOrNull() || sp.exit.DestroyedOrNull())
                            continue;

                        plans[sp.pawn] = sp.ToPlan();
                    }
                }

                savedPlans = null; // keep runtime memory tidy
            }
        }

        private struct Plan
        {
            public DoorTeleporter entry;
            public DoorTeleporter exit;
            public IntVec3 entryCell;
            public LocalTargetInfo finalDest;
            public PathEndMode finalPeMode;
            public int createdTick;
            public int draftedLockUntilTick;
            public bool inTransit;
            public int transitStartTick;
            public int transitTicksLeft;
            public IntVec3 transitTargetCell;
        }

        private class SavedPlan : IExposable
        {
            public Pawn pawn;

            public DoorTeleporter entry;
            public DoorTeleporter exit;

            public IntVec3 entryCell;
            public LocalTargetInfo finalDest;
            public PathEndMode finalPeMode;

            public int createdTick;
            public int draftedLockUntilTick;

            // Transit state
            public bool inTransit;
            public int transitStartTick;
            public int transitTicksLeft;
            public IntVec3 transitTargetCell;

            public SavedPlan() { }

            public SavedPlan(Pawn pawn, Plan p)
            {
                this.pawn = pawn;
                entry = p.entry;
                exit = p.exit;
                entryCell = p.entryCell;
                finalDest = p.finalDest;
                finalPeMode = p.finalPeMode;
                createdTick = p.createdTick;
                draftedLockUntilTick = p.draftedLockUntilTick;

                inTransit = p.inTransit;
                transitStartTick = p.transitStartTick;
                transitTicksLeft = p.transitTicksLeft;
                transitTargetCell = p.transitTargetCell;
            }

            public Plan ToPlan()
            {
                return new Plan
                {
                    entry = entry,
                    exit = exit,
                    entryCell = entryCell,
                    finalDest = finalDest,
                    finalPeMode = finalPeMode,
                    createdTick = createdTick,
                    draftedLockUntilTick = draftedLockUntilTick,

                    inTransit = inTransit,
                    transitStartTick = transitStartTick,
                    transitTicksLeft = transitTicksLeft,
                    transitTargetCell = transitTargetCell
                };
            }

            public void ExposeData()
            {
                Scribe_References.Look(ref pawn, "pawn");
                Scribe_References.Look(ref entry, "entry");
                Scribe_References.Look(ref exit, "exit");

                Scribe_Values.Look(ref entryCell, "entryCell");
                Scribe_TargetInfo.Look(ref finalDest, "finalDest");
                Scribe_Values.Look(ref finalPeMode, "finalPeMode");

                Scribe_Values.Look(ref createdTick, "createdTick");
                Scribe_Values.Look(ref draftedLockUntilTick, "draftedLockUntilTick");

                Scribe_Values.Look(ref inTransit, "inTransit");
                Scribe_Values.Look(ref transitStartTick, "transitStartTick");
                Scribe_Values.Look(ref transitTicksLeft, "transitTicksLeft");
                Scribe_Values.Look(ref transitTargetCell, "transitTargetCell");
            }
        }

        // Private: only SkipdoorTransitManager can see Plan
        private bool TryGetPlanInternal(Pawn pawn, out Plan plan)
        {
            plan = default;
            if (pawn == null) return false;
            if (!plans.TryGetValue(pawn, out plan)) return false;

            if (pawn.DestroyedOrNull() || pawn.Map != map || !pawn.Spawned || pawn.Dead ||
                plan.entry.DestroyedOrNull() || plan.exit.DestroyedOrNull())
            {
                plans.Remove(pawn);
                plan = default;
                return false;
            }
            return true;
        }

        private readonly Dictionary<Pawn, Plan> plans = new Dictionary<Pawn, Plan>();

        public SkipdoorTransitManager(Map map) : base(map) { }

        public void SetPlan(Pawn pawn, DoorTeleporter entry, DoorTeleporter exit, LocalTargetInfo finalDest, PathEndMode finalPeMode)
        {
            if (pawn == null || entry == null || exit == null) return;

            int now = Find.TickManager.TicksGame;
            int lockTicks = 30;

            plans[pawn] = new Plan
            {
                entry = entry,
                exit = exit,
                entryCell = entry.InteractionCell,
                finalDest = finalDest,
                finalPeMode = finalPeMode,
                createdTick = now,
                draftedLockUntilTick = pawn.Drafted ? (now + lockTicks) : 0
            };
        }

        public bool TryGetPlan(
            Pawn pawn,
            out DoorTeleporter entry,
            out DoorTeleporter exit,
            out LocalTargetInfo finalDest,
            out PathEndMode finalPeMode)
        {
            entry = null;
            exit = null;
            finalDest = default;
            finalPeMode = PathEndMode.None;

            if (!TryGetPlanInternal(pawn, out Plan plan))
                return false;

            int now = Find.TickManager.TicksGame;

            // Expire stale plans that never start transit (prevents infinite lingering plans)
            if (!plan.inTransit && plan.createdTick > 0 && now - plan.createdTick > PLAN_TTL_TICKS)
            {
                ClearPlan(pawn);
                return false;
            }

            // If pawn can no longer reach the entry cell, cancel the plan (check at most once/sec)
            if (!plan.inTransit && (now % UNREACHABLE_RECHECK_INTERVAL == 0))
            {
                if (!map.reachability.CanReach(pawn.Position, plan.entryCell, PathEndMode.OnCell,
                        TraverseMode.PassDoors, Danger.Deadly))
                {
                    ClearPlan(pawn);
                    return false;
                }
            }

            entry = plan.entry;
            exit = plan.exit;
            finalDest = plan.finalDest;
            finalPeMode = plan.finalPeMode;
            return true;
        }

        public bool TryGetPlanDraftData(
            Pawn pawn,
            out IntVec3 entryCell,
            out int draftedLockUntilTick,
            out LocalTargetInfo finalDest,
            out PathEndMode finalPeMode)
        {
            entryCell = IntVec3.Invalid;
            draftedLockUntilTick = 0;
            finalDest = default;
            finalPeMode = PathEndMode.None;

            if (!TryGetPlanInternal(pawn, out Plan plan))
                return false;

            entryCell = plan.entryCell;
            draftedLockUntilTick = plan.draftedLockUntilTick;
            finalDest = plan.finalDest;
            finalPeMode = plan.finalPeMode;
            return true;
        }

        public void ClearPlan(Pawn pawn)
        {
            if (pawn != null) plans.Remove(pawn);
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            int now = Find.TickManager.TicksGame;

            // Existing cleanup cadence...
            if (now % 250 == 0)
            {
                tmpPawns.Clear();
                foreach (var kv in plans)
                {
                    var pawn = kv.Key;
                    if (pawn.DestroyedOrNull() || pawn.Map != map || !pawn.Spawned || pawn.Dead)
                            tmpPawns.Add(pawn);
                }
                for (int i = 0; i < tmpPawns.Count; i++)
                    plans.Remove(tmpPawns[i]);

                // Keep the just-teleported cache tidy.
                tmpPawns.Clear();
                foreach (var kv in justTeleportedTick)
                {
                    var pawn = kv.Key;
                    if (pawn.DestroyedOrNull() || pawn.Map != map || !pawn.Spawned || pawn.Dead)
                        tmpPawns.Add(pawn);
                }
                for (int i = 0; i < tmpPawns.Count; i++)
                    justTeleportedTick.Remove(tmpPawns[i]);
            }

            // NEW: process in-transit teleports each tick
            if (plans.Count == 0) return;

            // Copy keys to avoid collection modification issues
            tmpPawns.Clear();
            foreach (var kv in plans)
                tmpPawns.Add(kv.Key);

            for (int i = 0; i < tmpPawns.Count; i++)
            {
                Pawn pawn = tmpPawns[i];
                if (!TryGetPlanInternal(pawn, out var plan)) continue;
                if (!plan.inTransit) continue;

                // If something goes wrong and transit never completes, don't keep it forever.
                if (plan.transitStartTick > 0 && now - plan.transitStartTick > TRANSIT_TTL_TICKS)
                {
                    ClearPlan(pawn);
                    continue;
                }

                // Defensive validity
                if (plan.entry.DestroyedOrNull() || plan.exit.DestroyedOrNull() || pawn.DestroyedOrNull() || pawn.Map != map)
                {
                    ClearPlan(pawn);
                    continue;
                }

                // Call the teleporter's own effect routine (Skipdoor overrides this)
                // This reproduces VPE's exact effect defs/sounds/timing.
                var targetCell = plan.transitTargetCell;

                try
                {
                    plan.entry.DoTeleportEffects(pawn, plan.transitTicksLeft, plan.exit.Map, ref targetCell, plan.exit);
                }
                catch
                {
                    // If effects fail, we still want the teleport to happen.
                }

                plan.transitTargetCell = targetCell;

                plan.transitTicksLeft--;

                // NOTE: Do NOT StopDead() every tick here.
                // Repeated StopDead can look like "idle/paused" to other job logic (including hauling mods)
                // and can cause unintuitive drop/unload decisions at the doorway.
                // We already StopDead() once when transit begins.

                if (plan.transitTicksLeft <= 0)
                {
                    // Preserve carried item across teleport. Some hauling logic may temporarily remove
                    // carried things during job re-evaluation; a portal transition should not force that.
                    Thing carriedThing = pawn.carryTracker?.CarriedThing;
                    bool manuallyPreserved = false;
                    if (carriedThing != null)
                    {
                        pawn.carryTracker.innerContainer.Remove(carriedThing);
                        manuallyPreserved = true;
                    }

                    // Execute teleport
                    plan.entry.Teleport(pawn, plan.exit.Map, plan.transitTargetCell);

                    if (manuallyPreserved && carriedThing != null)
                    {
                        if (pawn.Spawned && !pawn.Dead && !pawn.Downed)
                            pawn.carryTracker.innerContainer.TryAdd(carriedThing);
                        else
                            GenSpawn.Spawn(carriedThing, pawn.Position, pawn.Map);
                    }

                    // Mark as just teleported for a brief grace window.
                    justTeleportedTick[pawn] = now;

                    // Clear plan before follow-up movement
                    ClearPlan(pawn);

                    // Drafted: re-issue player-forced goto so they keep moving
                    if (pawn.Drafted && pawn.jobs != null && plan.finalDest.IsValid && plan.finalDest.Cell.IsValid)
                    {
                        var j = JobMaker.MakeJob(JobDefOf.Goto, plan.finalDest.Cell);
                        j.playerForced = true;
                        pawn.jobs.TryTakeOrderedJob(j, JobTag.Misc);
                    }
                    else
                    {
                        if (!plan.finalDest.IsValid)
                            continue;

                        // If destination is a cell, ensure it's in bounds on the current map
                        if (!plan.finalDest.HasThing && plan.finalDest.Cell.IsValid && !plan.finalDest.Cell.InBounds(pawn.Map))
                            continue;

                        // If destination is a thing, ensure it still exists
                        if (plan.finalDest.HasThing)
                        {
                            Thing t = plan.finalDest.Thing;
                            if (t == null || t.DestroyedOrNull())
                                continue;
                        }

                        pawn.pather?.StartPath(plan.finalDest, plan.finalPeMode);
                    }
                }
                else
                {
                    // Write back updated plan state
                    plans[pawn] = plan;
                }
            }
        }

        public bool TryExecuteTeleportIfAtEntry(Pawn pawn)
        {
            int now = Find.TickManager.TicksGame;

            if (!TryGetPlanInternal(pawn, out Plan plan))
                return false;

            // If already in transit, we'll advance it elsewhere
            if (plan.inTransit)
                return false;

            
            // entry unreachable (check once/sec)
            if (!plan.inTransit && (now % UNREACHABLE_RECHECK_INTERVAL == 0))
            {
                if (!map.reachability.CanReach(pawn.Position, plan.entryCell, PathEndMode.OnCell,
                        TraverseMode.PassDoors, Danger.Deadly))
                {
                    ClearPlan(pawn);
                    return false;
                }
            }

            if (pawn.Position != plan.entryCell)
                return false;

            // expire stale plans
            if (!plan.inTransit && plan.createdTick > 0 && now - plan.createdTick > PLAN_TTL_TICKS)
            {
                ClearPlan(pawn);
                return false;
            }

            pawn.pather?.StopDead();
            pawn.stances?.CancelBusyStanceSoft();

            // Start a 15-tick VPE-like countdown (matches Skipdoor.DoTeleportEffects)
            plan.inTransit = true;
            plan.transitStartTick = now;
            plan.transitTicksLeft = 15;
            plan.transitTargetCell = plan.exit.InteractionCell; // will be adjusted by DoTeleportEffects at tick 15

            plans[pawn] = plan; // write back once
            return true;
        }
    }
}
