using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using VEF.Buildings;
using Verse;
using Verse.AI;

namespace SkipdoorPathing
{
    public class SkipdoorTransitManager : MapComponent
    {
        private List<SavedPlan> savedPlans;

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

            // Transit state (if you add it)
            public bool inTransit;
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

            // Existing cleanup cadence...
            if (Find.TickManager.TicksGame % 250 == 0)
            {
                var toRemove = new List<Pawn>();
                foreach (var kv in plans)
                {
                    var pawn = kv.Key;
                    if (pawn.DestroyedOrNull() || pawn.Map != map || !pawn.Spawned) toRemove.Add(pawn);
                }
                foreach (var p in toRemove) plans.Remove(p);
            }

            // NEW: process in-transit teleports each tick
            if (plans.Count == 0) return;

            // Copy keys to avoid collection modification issues
            var pawns = plans.Keys.ToList();
            foreach (var pawn in pawns)
            {
                if (!TryGetPlanInternal(pawn, out var plan)) continue;
                if (!plan.inTransit) continue;

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

                // Hold pawn still during the countdown
                pawn.pather?.StopDead();

                if (plan.transitTicksLeft <= 0)
                {
                    // Execute teleport
                    plan.entry.Teleport(pawn, plan.exit.Map, plan.transitTargetCell);

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
                        // Avoid "pathing to destroyed thing" red errors (finalDest can be a Thing that vanished mid-transit)
                        if (plan.finalDest.IsValid)
                        {
                            if (plan.finalDest.HasThing)
                            {
                                Thing t = plan.finalDest.Thing;
                                if (t == null || t.DestroyedOrNull())
                                {
                                    // Let vanilla/jobdriver re-resolve next tick instead of forcing a bad path.
                                    return;
                                }
                            }

                            pawn.pather?.StartPath(plan.finalDest, plan.finalPeMode);
                        }
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
            if (!TryGetPlanInternal(pawn, out Plan plan))
                return false;

            // If already in transit, we'll advance it elsewhere
            if (plan.inTransit)
                return false;

            if (pawn.Position != plan.entryCell)
                return false;

            pawn.pather?.StopDead();
            pawn.stances?.CancelBusyStanceSoft();

            // Start a 15-tick VPE-like countdown (matches Skipdoor.DoTeleportEffects)
            plan.inTransit = true;
            plan.transitTicksLeft = 15;
            plan.transitTargetCell = plan.exit.InteractionCell; // will be adjusted by DoTeleportEffects at tick 15

            plans[pawn] = plan; // IMPORTANT: write back

            return true;
        }
    }
}
