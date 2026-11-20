using System.Collections.Generic;
using System.Globalization;

namespace JunctionRules.Core
{
    // JR-T-025 (1.1.1): "why is this car slowing". For ONE car (the game's selected vehicle, else one the API names) the
    // copy's navigation job records which limit bound the car's speed this step: the binding (smallest) limit and the code
    // path that set it, game code and Junction Rules code alike. Every place in the copy that lowers the car's limit tags
    // itself (blocks S2-55 .. S2-59, plus record lines in existing blocks); a limit lowered by a path with no tag is reported as "stock/other", never as a
    // Junction Rules rule (WhyTrack.Resolve). Off-cost: with no car tracked the job compares one entity per car and does
    // nothing else; only the tracked car writes the one shared slot (WhySlot). Pure helpers here (no Unity, Burst-safe
    // structs); job side Stage2/JRWhy.cs, main thread Stage2/JRStage2Bridge.cs, API JunctionRulesApi.WhySlowed.

    /// <summary>
    /// Which code path set the binding limit. Values are stable (API / log); names in <see cref="WhyRules.Id"/>.
    /// Stock = the game's own car code (copied unchanged); Jr = a Junction Rules rule.
    /// </summary>
    public enum WhyRule : byte
    {
        /// <summary>No record (nothing tracked yet).</summary>
        None = 0,
        /// <summary>Nothing slows the car: its own top speed / acceleration range this step.</summary>
        Free = 1,
        /// <summary>Lane speed limit, curve speed, road condition, or the half speed behind a reserved lane (game's drive speed).</summary>
        StockLaneSpeed = 2,
        /// <summary>A caution zone on the lane (the game's half / 0.8 speed limit there).</summary>
        StockCaution = 3,
        /// <summary>A STOP sign: stop at the line.</summary>
        StockStopSign = 4,
        /// <summary>A red / amber light (or level crossing): stop at the line.</summary>
        StockSignal = 5,
        /// <summary>The game's junction room check (CheckSpace): no room past the junction, waits at its entry.</summary>
        StockJunctionRoom = 6,
        /// <summary>Following / stopping behind the vehicle ahead on its own path (its lane, or the parking space it drives to).</summary>
        StockCarAhead = 7,
        /// <summary>An oncoming vehicle on a two-way lane of its path.</summary>
        StockOncoming = 8,
        /// <summary>Gives way at an overlap: a conflicting lane is reserved with priority (the game's give-way; extra = priority delta).</summary>
        StockGiveWay = 9,
        /// <summary>The game's slow-down at a priority overlap (to half the lane limit).</summary>
        StockGiveWaySlow = 10,
        /// <summary>The game's give-way to a vehicle asking for space (reservation priority 96).</summary>
        StockGiveWaySpace = 11,
        /// <summary>Brakes for a vehicle on a conflicting lane (the game's check of vehicles on crossing / merging lanes; extra 1 = merging).</summary>
        StockConflictVehicle = 12,
        /// <summary>A person on a crossing of its path (the game's pedestrian check, creep or stop).</summary>
        StockPedestrian = 13,
        /// <summary>Its target / the end of its known path is near (the game's target braking).</summary>
        StockTargetEnd = 15,
        /// <summary>The game's collision check (VehicleCollisionIterator: something physically in the way).</summary>
        StockCollision = 16,
        /// <summary>Its navigation target is less than one step away (the game's step cap).</summary>
        StockTargetDistance = 17,
        /// <summary>Turning / reversing: the target lies too far to the side or behind (the game's steering limit).</summary>
        StockSteering = 18,
        /// <summary>The limit was lowered by a path with no tag: the game's own code, not named more closely.</summary>
        StockOther = 19,

        /// <summary>Junction Rules whole-junction gap: waits before its first crossing until the whole path is clear.</summary>
        JrWholeJunctionGap = 40,
        /// <summary>Junction Rules safety re-check: stopped before the first uncrossed point while crossing.</summary>
        JrSafetyRecheck = 41,
        /// <summary>Junction Rules: people on a crossing of its path, waits at its waiting spot.</summary>
        JrPeopleWaitSpot = 42,
        /// <summary>Junction Rules "never push through people": full stop for a person (the game would creep).</summary>
        JrNeverPushPeople = 43,
        /// <summary>Junction Rules: no creeping through a cyclist on a crossing (the game's ignore-blocker creep refused).</summary>
        JrNoPushCyclist = 44,
        /// <summary>Junction Rules "cars never stop on a zebra": held before a zebra section (no room past it).</summary>
        JrZebraSection = 45,
        /// <summary>Junction Rules yellow box: held at the line, no room past the box (junction box or exit band).</summary>
        JrBoxRoom = 46,
        /// <summary>Junction Rules yellow box, look-past refused (extra = <see cref="LookPastRefusal"/> code).</summary>
        JrBoxLookPastRefused = 47,
        /// <summary>Junction Rules yellow box, UK right-turn: waits at its turn spot, no room on its exit.</summary>
        JrBoxTurnSpot = 48,
        /// <summary>Junction Rules yellow box: turning into a building, its way in is full.</summary>
        JrBoxEntering = 49,
        /// <summary>Junction Rules yellow box: people on the box's exit zebra, waits at the line.</summary>
        JrExitZebraWait = 50,
        /// <summary>Junction Rules: leaving a building, gives way (gap) to road traffic.</summary>
        JrExitGiveWay = 51,
        /// <summary>Junction Rules: leaving a building into a boxed exit, no room past the box.</summary>
        JrExitBoxRoom = 52,
        /// <summary>Junction Rules: turning into a building, gives way (gap) to the lanes it crosses.</summary>
        JrEntryGiveWay = 53,
        /// <summary>A Junction Rules hold whose rule was not named more closely (the hook is known, the branch is not).</summary>
        JrOther = 54,
        /// <summary>Junction Rules 1.0.1 soft zebra: held before a signalised zebra (people not on green): no room past what it crosses.</summary>
        JrSoftZebra = 55,
        /// <summary>Junction Rules 1.4.0 (JR-T-033): people on a zebra of the path; the car waits right before that zebra (drives up).</summary>
        JrPeopleWaitZebra = 56,
    }

    /// <summary>JR-T-025: what the blocker is (main thread, from the entity's components).</summary>
    public enum WhyKind : byte { None = 0, Vehicle = 1, Person = 2, Cyclist = 3, Other = 4, Gone = 5 }

    /// <summary>JR-T-025: why a yellow box look-past gave no room (RoomScan.Refusal; extra of <see cref="WhyRule.JrBoxLookPastRefused"/>).</summary>
    public enum LookPastRefusal : byte
    {
        None = 0,
        /// <summary>A vehicle it follows through the box is slower than LookPastMinSpeed.</summary>
        SlowAhead = 1,
        /// <summary>The room it needs ends beyond a large vehicle that ends the view.</summary>
        ViewEndsAtLarge = 2,
        /// <summary>The room it needs ends beyond what the driver sees (radius / path read). 1.2.0 only: JR-T-029 (1.3.0) no longer sets it.</summary>
        BeyondSight = 3,
        /// <summary>A building blocks the straight line of sight to a credited vehicle (1.2.0 also tested the end of the room; 1.3.0 does not).</summary>
        Building = 4,
        /// <summary>A large vehicle queued ahead switched the look-past off for this decision (free-now verdict).</summary>
        LargeQueuedAhead = 5,
        /// <summary>JR-T-029: the vehicles ahead packed behind the standing one / red line had to skip a zebra: the car's room is gone.</summary>
        QueuePushedByZebra = 6,
        /// <summary>JR-T-029: as above, a further yellow box (junction, exit / entrance band) pushed the packed queue back.</summary>
        QueuePushedByBox = 7,
        /// <summary>JR-T-029: as above, another junction's interior pushed the packed queue back.</summary>
        QueuePushedByJunction = 8,
    }

    public static class WhyRules
    {
        /// <summary>Stable id for the API / log ("stock_signal", "jr_box_room", ...).</summary>
        public static string Id(WhyRule r)
        {
            switch (r)
            {
                case WhyRule.None: return "none";
                case WhyRule.Free: return "free";
                case WhyRule.StockLaneSpeed: return "stock_lane_speed";
                case WhyRule.StockCaution: return "stock_caution";
                case WhyRule.StockStopSign: return "stock_stop_sign";
                case WhyRule.StockSignal: return "stock_signal";
                case WhyRule.StockJunctionRoom: return "stock_junction_room";
                case WhyRule.StockCarAhead: return "stock_car_ahead";
                case WhyRule.StockOncoming: return "stock_oncoming";
                case WhyRule.StockGiveWay: return "stock_give_way";
                case WhyRule.StockGiveWaySlow: return "stock_give_way_slow";
                case WhyRule.StockGiveWaySpace: return "stock_give_way_space";
                case WhyRule.StockConflictVehicle: return "stock_conflict_vehicle";
                case WhyRule.StockPedestrian: return "stock_pedestrian";
                case WhyRule.StockTargetEnd: return "stock_target_end";
                case WhyRule.StockCollision: return "stock_collision";
                case WhyRule.StockTargetDistance: return "stock_target_distance";
                case WhyRule.StockSteering: return "stock_steering";
                case WhyRule.StockOther: return "stock_other";
                case WhyRule.JrWholeJunctionGap: return "jr_whole_junction_gap";
                case WhyRule.JrSafetyRecheck: return "jr_safety_recheck";
                case WhyRule.JrPeopleWaitSpot: return "jr_people_wait_spot";
                case WhyRule.JrNeverPushPeople: return "jr_never_push_people";
                case WhyRule.JrNoPushCyclist: return "jr_no_push_cyclist";
                case WhyRule.JrZebraSection: return "jr_zebra_section";
                case WhyRule.JrBoxRoom: return "jr_box_room";
                case WhyRule.JrBoxLookPastRefused: return "jr_box_look_past_refused";
                case WhyRule.JrBoxTurnSpot: return "jr_box_turn_spot";
                case WhyRule.JrBoxEntering: return "jr_box_entering";
                case WhyRule.JrExitZebraWait: return "jr_exit_zebra_wait";
                case WhyRule.JrExitGiveWay: return "jr_exit_give_way";
                case WhyRule.JrExitBoxRoom: return "jr_exit_box_room";
                case WhyRule.JrEntryGiveWay: return "jr_entry_give_way";
                case WhyRule.JrOther: return "jr_other";
                case WhyRule.JrSoftZebra: return "jr_soft_zebra";
                case WhyRule.JrPeopleWaitZebra: return "jr_people_wait_zebra";
                default: return "rule_" + ((int)r).ToString(CultureInfo.InvariantCulture);
            }
        }

        /// <summary>Short player-facing text ("Junction Rules: ..." in the log).</summary>
        public static string Text(WhyRule r)
        {
            switch (r)
            {
                case WhyRule.None: return "no reading yet";
                case WhyRule.Free: return "nothing slows it (own top speed / acceleration)";
                case WhyRule.StockLaneSpeed: return "game: lane speed limit / curve / road condition";
                case WhyRule.StockCaution: return "game: caution zone";
                case WhyRule.StockStopSign: return "game: STOP sign, stops at the line";
                case WhyRule.StockSignal: return "game: red / amber light";
                case WhyRule.StockJunctionRoom: return "game: no room past the junction (game's own room check)";
                case WhyRule.StockCarAhead: return "game: following the vehicle ahead";
                case WhyRule.StockOncoming: return "game: oncoming vehicle on a two-way lane";
                case WhyRule.StockGiveWay: return "game: gives way (a conflicting lane is reserved with priority)";
                case WhyRule.StockGiveWaySlow: return "game: slows at a priority overlap";
                case WhyRule.StockGiveWaySpace: return "game: gives way to a vehicle asking for space";
                case WhyRule.StockConflictVehicle: return "game: vehicle on a crossing / merging lane";
                case WhyRule.StockPedestrian: return "game: person on a crossing of its path";
                case WhyRule.StockTargetEnd: return "game: near its target / end of known path";
                case WhyRule.StockCollision: return "game: collision check (something physically in the way)";
                case WhyRule.StockTargetDistance: return "game: navigation target less than one step away";
                case WhyRule.StockSteering: return "game: turning / reversing limit";
                case WhyRule.StockOther: return "stock/other: game code not named more closely";
                case WhyRule.JrWholeJunctionGap: return "Junction Rules: whole-junction gap, waits before its first crossing";
                case WhyRule.JrSafetyRecheck: return "Junction Rules: safety re-check while crossing";
                case WhyRule.JrPeopleWaitSpot: return "Junction Rules: people on a crossing, waits at its spot";
                case WhyRule.JrNeverPushPeople: return "Junction Rules: never push through people (full stop)";
                case WhyRule.JrNoPushCyclist: return "Junction Rules: no creeping through a cyclist on a crossing";
                case WhyRule.JrZebraSection: return "Junction Rules: never stop on a zebra (no room past it)";
                case WhyRule.JrBoxRoom: return "Junction Rules: yellow box, no room past the box";
                case WhyRule.JrBoxLookPastRefused: return "Junction Rules: yellow box, look-past refused";
                case WhyRule.JrBoxTurnSpot: return "Junction Rules: yellow box, right-turner waits at its turn spot";
                case WhyRule.JrBoxEntering: return "Junction Rules: yellow box, way into the building full";
                case WhyRule.JrExitZebraWait: return "Junction Rules: yellow box, people on its exit zebra";
                case WhyRule.JrExitGiveWay: return "Junction Rules: leaving a building, gives way";
                case WhyRule.JrExitBoxRoom: return "Junction Rules: leaving a building, no room on the lane it joins / past its box";
                case WhyRule.JrEntryGiveWay: return "Junction Rules: turning into a building, gives way";
                case WhyRule.JrOther: return "Junction Rules: hold (rule not named more closely)";
                case WhyRule.JrSoftZebra: return "Junction Rules: soft zebra (signalised), no room past it";
                case WhyRule.JrPeopleWaitZebra: return "Junction Rules: people on a zebra, waits right before it";
                default: return Id(r);
            }
        }

        public static bool IsJr(WhyRule r) => (byte)r >= (byte)WhyRule.JrWholeJunctionGap;

        public static string KindId(WhyKind k)
        {
            switch (k)
            {
                case WhyKind.Vehicle: return "vehicle";
                case WhyKind.Person: return "person";
                case WhyKind.Cyclist: return "cyclist";
                case WhyKind.Other: return "other";
                case WhyKind.Gone: return "gone";
                default: return "none";
            }
        }

        public static string RefusalId(LookPastRefusal r)
        {
            switch (r)
            {
                case LookPastRefusal.SlowAhead: return "slow_vehicle_ahead";
                case LookPastRefusal.ViewEndsAtLarge: return "view_ends_at_large_vehicle";
                case LookPastRefusal.BeyondSight: return "room_beyond_sight";
                case LookPastRefusal.Building: return "building_blocks_sight";
                case LookPastRefusal.LargeQueuedAhead: return "large_vehicle_queued_ahead";
                case LookPastRefusal.QueuePushedByZebra: return "queue_pushed_back_by_zebra";
                case LookPastRefusal.QueuePushedByBox: return "queue_pushed_back_by_box";
                case LookPastRefusal.QueuePushedByJunction: return "queue_pushed_back_by_junction";
                default: return "none";
            }
        }

        /// <summary>A hook's noted rule (0 = the hook named none) or the given fallback.</summary>
        public static WhyRule Hook(byte noted, WhyRule fallback) => noted != 0 ? (WhyRule)noted : fallback;

        /// <summary>
        /// The rule for a give-way site the whole-junction rule may have changed: the game's own value when the rule left it
        /// unchanged, else the whole-junction gap (deciding) or its safety re-check (crossing).
        /// </summary>
        public static WhyRule GiveWay(bool changedByJr, bool safetyPhase, WhyRule stock) =>
            !changedByJr ? stock : safetyPhase ? WhyRule.JrSafetyRecheck : WhyRule.JrWholeJunctionGap;

        /// <summary>The stock rule of the junction-entry chain (IterateNextLane): stop / signal / room / plain speed.</summary>
        public static WhyRule EntryChain(bool stopped, int blockerType)
        {
            if (!stopped) return WhyRule.StockLaneSpeed;
            switch (blockerType)
            {
                case BlockerSignal: return WhyRule.StockSignal;
                case BlockerContinuing: return WhyRule.StockJunctionRoom;
                case BlockerLimit: return WhyRule.StockStopSign;
                default: return WhyRule.StockOther;
            }
        }

        /// <summary>
        /// The stock rule of a limit no site tagged, from the game's own blocker record of the binding site (Game.Vehicles
        /// BlockerType + whether it names an entity). Sites that would read ambiguously are tagged where they set the limit
        /// (junction entry chain, priority slow-down, give-way and conflict sites, people, Junction Rules hooks).
        /// </summary>
        public static WhyRule Stock(int blockerType, bool hasBlocker)
        {
            switch (blockerType)
            {
                case 0: return WhyRule.StockTargetEnd;   // IterateTarget
                case BlockerContinuing: return hasBlocker ? WhyRule.StockCarAhead : WhyRule.StockGiveWay;
                case 2: return hasBlocker ? WhyRule.StockConflictVehicle : WhyRule.StockGiveWay;   // Crossing
                case BlockerSignal: return WhyRule.StockSignal;
                case 4: return WhyRule.StockPedestrian;   // Temporary (CheckPedestrian)
                case BlockerLimit: return WhyRule.StockLaneSpeed;
                case 6: return WhyRule.StockCaution;
                case BlockerOncoming: return WhyRule.StockOncoming;
                default: return WhyRule.StockOther;
            }
        }

        /// <summary>Game.Vehicles.BlockerType values (decompile Game.Vehicles/BlockerType.cs; JunctionWatch.BlockerTypeName).</summary>
        public const int BlockerContinuing = 1, BlockerSignal = 3, BlockerLimit = 5, BlockerOncoming = 8;
    }

    /// <summary>JR-T-025: the binding limit of one car-step and where it came from (Burst-safe value type).</summary>
    public struct WhyRecord
    {
        public WhyRule Rule;
        /// <summary>The entity the limit waits for (JunctionWatch.Key; 0 = none named).</summary>
        public long Blocker;
        /// <summary>The lane (or node lane) the limit was set on (0 = unknown).</summary>
        public long Lane;
        /// <summary>Distance (m) from the car's front to the hold point; NaN = not known.</summary>
        public float Distance;
        /// <summary>The binding limit (m/s).</summary>
        public float Limit;
        /// <summary>Rule-specific number (box room short by m, look-past refusal code, priority delta, ...); NaN = none.</summary>
        public float Extra;
    }

    /// <summary>
    /// JR-T-025: the per-car tracker the copy carries (iterator field m_Why, job scratch m_JRWhyScratch: one element, only the tracked car's worker touches it). The iterator's limit
    /// only goes down; every site that lowers it calls <see cref="Bind"/> right where it assigns (the site already decided
    /// it binds), or <see cref="Offer"/> where the comparison is not the site's own. Binding = the smallest limit; ties
    /// keep the first (the game's own "&lt;" test). <see cref="Resolve"/> checks the final limit against the record: a
    /// limit lowered (or restored) by an untagged path becomes <see cref="WhyRule.StockOther"/>.
    /// </summary>
    public struct WhyTrack
    {
        public WhyRecord Bound;

        /// <summary>The car's limit before the game's turning / reversing tail (NaN = the step returned before it).</summary>
        public float BeforeSteer;

        /// <summary>Final limit and record differ by more than this (m/s): the record does not explain the limit.</summary>
        public const float Tolerance = 0.001f;

        public void Begin(float limit)
        {
            Bound = default;
            Bound.Rule = WhyRule.Free;
            Bound.Limit = limit;
            Bound.Distance = float.NaN;
            Bound.Extra = float.NaN;
            BeforeSteer = float.NaN;
        }

        public void Bind(WhyRule rule, float limit, long blocker, long lane, float distance, float extra)
        {
            Bound.Rule = rule;
            Bound.Limit = limit;
            Bound.Blocker = blocker;
            Bound.Lane = lane;
            Bound.Distance = distance;
            Bound.Extra = extra;
        }

        /// <summary>Binds only below the current record (strict: a tie keeps the earlier source). True = bound.</summary>
        public bool Offer(WhyRule rule, float limit, long blocker, long lane, float distance, float extra)
        {
            if (!(limit < Bound.Limit)) return false;
            Bind(rule, limit, blocker, lane, distance, extra);
            return true;
        }

        /// <summary>
        /// A limit the copy lowered with no tag between two tagged points: classified from the game's blocker record of the
        /// binding site (<see cref="WhyRules.Stock"/>). Nothing to do when the limit is still the recorded one.
        /// </summary>
        public void Settle(float limit, int blockerType, long blocker, long lane)
        {
            if (limit < Bound.Limit) Bind(WhyRules.Stock(blockerType, blocker != 0), limit, blocker, lane, float.NaN, float.NaN);
        }

        /// <summary>
        /// End of the car's step: <paramref name="beforeSteer"/> = the limit before the game's turning / reversing tail (NaN =
        /// the step returned before it). A tail that changed the limit binds as <see cref="WhyRule.StockSteering"/>; any other
        /// unexplained difference is <see cref="WhyRule.StockOther"/>.
        /// </summary>
        public void Finish(float finalLimit, float beforeSteer)
        {
            if (!float.IsNaN(beforeSteer))
            {
                float d = finalLimit - beforeSteer;
                if (d > Tolerance || d < -Tolerance)
                {
                    Bind(WhyRule.StockSteering, finalLimit < 0f ? -finalLimit : finalLimit, 0, Bound.Lane, float.NaN, finalLimit < 0f ? 1f : float.NaN);
                    return;
                }
            }
            Resolve(finalLimit, WhyRule.StockOther);
        }

        /// <summary>
        /// The car's final limit for the step: when the record does not explain it (an untagged path lowered it, or a tagged
        /// value was undone), the reason is <paramref name="fallback"/> (StockOther, or StockSteering for the steering tail).
        /// Negative limits (reversing) are compared by size.
        /// </summary>
        public void Resolve(float finalLimit, WhyRule fallback)
        {
            float f = finalLimit < 0f ? -finalLimit : finalLimit;
            float d = f - Bound.Limit;
            if (d > Tolerance || d < -Tolerance || Bound.Rule == WhyRule.None || float.IsNaN(Bound.Limit))
                Bind(fallback, f, 0, 0, float.NaN, float.NaN);
            else Bound.Limit = f;
        }
    }

    /// <summary>JR-T-025: access to the shared slot (job: a pointer store; tests: an array). Burst-safe as a struct generic.</summary>
    public interface IWhySlotStore
    {
        long Read(int i);
        void Write(int i, long v);
        /// <summary>The sequence write (a full barrier on the job side: Interlocked.Exchange).</summary>
        void WriteSeq(long v);
    }

    /// <summary>
    /// JR-T-025: the one shared slot (a persistent NativeArray of longs owned by the copy's bridge). [0] is reserved and
    /// unused (the tracked car reaches the job as an entity, S2-23; nothing tracked = no car writes anything). Single writer: the job, for the
    /// tracked car only. Seqlock: the writer makes the sequence odd, writes, makes it even; the main thread accepts only an
    /// even sequence that did not change while it read. Values are fixed point (no float bit casts).
    /// </summary>
    public static class WhySlot
    {
        public const int Tracked = 0, Seq = 1, Frame = 2, Car = 3, Rule = 4, Blocker = 5, Lane = 6, DistLimit = 7, ExtraSpeed = 8, Pos = 9, FinalGameType = 10, GameBlocker = 11;
        public const int Length = 12;

        /// <summary>NaN / out of range stored as this.</summary>
        public const int NaNFixed = int.MinValue;

        /// <summary>Scales: metres and m/s in mm (mm/s), extra in thousandths, positions in cm.</summary>
        public const float MilliScale = 1000f, PosScale = 100f;

        /// <summary>The off-cost gate: a car writes only when something is tracked and it is that car.</summary>
        public static bool ShouldWrite(long tracked, long car) => tracked != 0 && tracked == car;

        /// <summary>value * scale rounded to an int; NaN / infinite / out of range = <see cref="NaNFixed"/>.</summary>
        public static int ToFixed(float v, float scale)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return NaNFixed;
            double s = (double)v * scale;
            if (s >= int.MaxValue || s <= int.MinValue + 1) return NaNFixed;
            return (int)(s >= 0 ? s + 0.5 : s - 0.5);
        }

        public static float FromFixed(int v, float scale) => v == NaNFixed ? float.NaN : (float)(v / (double)scale);

        public static long Pack2(int hi, int lo) => ((long)hi << 32) | (uint)lo;
        public static int Hi(long v) => (int)(v >> 32);
        public static int Lo(long v) => (int)(v & 0xFFFFFFFFL);

        /// <summary>Job side: one car-step of the tracked car (seqlock write). The caller already checked <see cref="ShouldWrite"/>.</summary>
        public static void Write<S>(ref S store, uint frame, long car, in WhyRecord r, float speed, float x, float z, float finalLimit, long gameBlocker, int gameType)
            where S : struct, IWhySlotStore
        {
            long seq = store.Read(Seq);
            if ((seq & 1) == 0) seq++;
            store.WriteSeq(seq);   // odd: writing
            store.Write(Frame, frame);
            store.Write(Car, car);
            store.Write(Rule, (long)r.Rule);
            store.Write(Blocker, r.Blocker);
            store.Write(Lane, r.Lane);
            store.Write(DistLimit, Pack2(ToFixed(r.Distance, MilliScale), ToFixed(r.Limit, MilliScale)));
            store.Write(ExtraSpeed, Pack2(ToFixed(r.Extra, MilliScale), ToFixed(speed, MilliScale)));
            store.Write(Pos, Pack2(ToFixed(x, PosScale), ToFixed(z, PosScale)));
            store.Write(FinalGameType, Pack2(ToFixed(finalLimit, MilliScale), gameType));
            store.Write(GameBlocker, gameBlocker);
            store.WriteSeq(seq + 1);   // even: done
        }

        /// <summary>Main thread: one consistent sample, or false (nothing written yet, a write in progress, or torn).</summary>
        public static bool TryRead<S>(ref S store, out WhySample s) where S : struct, IWhySlotStore
        {
            s = default;
            long a = store.Read(Seq);
            if (a == 0 || (a & 1) != 0) return false;
            s.Seq = a;
            s.Frame = (uint)store.Read(Frame);
            s.Car = store.Read(Car);
            s.Rule = (WhyRule)(byte)store.Read(Rule);
            s.Blocker = store.Read(Blocker);
            s.Lane = store.Read(Lane);
            long dl = store.Read(DistLimit), es = store.Read(ExtraSpeed), p = store.Read(Pos), fg = store.Read(FinalGameType);
            s.Distance = FromFixed(Hi(dl), MilliScale);
            s.Limit = FromFixed(Lo(dl), MilliScale);
            s.Extra = FromFixed(Hi(es), MilliScale);
            s.Speed = FromFixed(Lo(es), MilliScale);
            s.X = FromFixed(Hi(p), PosScale);
            s.Z = FromFixed(Lo(p), PosScale);
            s.FinalLimit = FromFixed(Hi(fg), MilliScale);
            s.GameBlockerType = Lo(fg);
            s.GameBlocker = store.Read(GameBlocker);
            return store.Read(Seq) == a;
        }
    }

    /// <summary>JR-T-025: an array store (tests, and anything off the job).</summary>
    public struct WhyArrayStore : IWhySlotStore
    {
        public long[] Data;
        public int Writes;
        public WhyArrayStore(long[] data) { Data = data; Writes = 0; }
        public long Read(int i) => Data[i];
        public void Write(int i, long v) { Data[i] = v; Writes++; }
        public void WriteSeq(long v) { Data[WhySlot.Seq] = v; Writes++; }
    }

    /// <summary>JR-T-025: one reading of the tracked car (main thread).</summary>
    public struct WhySample
    {
        public long Seq;
        public uint Frame;
        public long Car;
        public WhyRule Rule;
        public long Blocker;
        public long Lane;
        public float Distance, Limit, Extra, Speed, X, Z, FinalLimit;
        /// <summary>The game's Blocker component after the step (entity, BlockerType byte).</summary>
        public long GameBlocker;
        public int GameBlockerType;
        /// <summary>Filled on the main thread (entity components): what the blocker is and where.</summary>
        public WhyKind BlockerKind;
        public float BlockerX, BlockerZ;

        /// <summary>A reason change: another rule or another blocker.</summary>
        public static bool SameReason(in WhySample a, in WhySample b) => a.Rule == b.Rule && a.Blocker == b.Blocker;
    }

    /// <summary>
    /// JR-T-025, main thread: the readout book. Keeps the latest sample of the tracked car, its last <see cref="MaxChanges"/>
    /// reason changes (rule, limit, blocker, distance, frame each: a double brake reads back step by step), and writes log
    /// lines on a tracking change and on each reason change, at most <see cref="MaxLinesPerSecond"/> a second (a dropped line
    /// is still in the history; the next line says how many were dropped). Per car: the histories of the last
    /// <see cref="MaxCars"/> cars tracked before are kept, and come back when a car is tracked again.
    /// </summary>
    public sealed class WhyBook
    {
        public const int MaxChanges = 20;
        public const int MaxLinesPerSecond = 2;
        public const int MaxCars = 8;

        private readonly LineRateLimiter m_Limiter = new LineRateLimiter(MaxLinesPerSecond);
        private List<WhySample> m_Changes = new List<WhySample>();
        private long m_LastSeq;
        /// <summary>Earlier cars' histories, oldest first.</summary>
        private readonly List<KeyValuePair<long, List<WhySample>>> m_Past = new List<KeyValuePair<long, List<WhySample>>>();

        /// <summary>Cars with a kept history (the tracked one first), for the API.</summary>
        public IEnumerable<long> KnownCars
        {
            get
            {
                if (Car != 0) yield return Car;
                for (int i = m_Past.Count - 1; i >= 0; i--) yield return m_Past[i].Key;
            }
        }

        /// <summary>A car's reason changes (oldest first): the tracked car's, a kept one, or null.</summary>
        public IReadOnlyList<WhySample> HistoryOf(long car)
        {
            if (car != 0 && car == Car) return m_Changes;
            for (int i = 0; i < m_Past.Count; i++) if (m_Past[i].Key == car) return m_Past[i].Value;
            return null;
        }

        private List<WhySample> TakePast(long car)
        {
            for (int i = 0; i < m_Past.Count; i++)
            {
                if (m_Past[i].Key != car) continue;
                List<WhySample> l = m_Past[i].Value;
                m_Past.RemoveAt(i);
                return l;
            }
            return null;
        }

        public long Car { get; private set; }
        /// <summary>"selection", "api" or "none".</summary>
        public string Source { get; private set; } = "none";
        public bool HasCurrent { get; private set; }
        public WhySample Current { get; private set; }
        public IReadOnlyList<WhySample> Changes => m_Changes;
        public long DroppedLines { get; private set; }

        /// <summary>The car to track (0 = none). Returns a log line on a change of car or source, else null.</summary>
        public string Track(long car, string source)
        {
            if (car == Car && (car == 0 || source == Source)) return null;
            long prev = Car;
            Car = car;
            Source = car == 0 ? "none" : source;
            if (car != prev)
            {
                if (prev != 0 && m_Changes.Count > 0)
                {
                    TakePast(prev);
                    m_Past.Add(new KeyValuePair<long, List<WhySample>>(prev, m_Changes));
                    if (m_Past.Count > MaxCars) m_Past.RemoveAt(0);
                }
                m_Changes = (car != 0 ? TakePast(car) : null) ?? new List<WhySample>();
                HasCurrent = false;
                Current = default;
                m_LastSeq = 0;
            }
            return car == 0 ? "why slowed: tracking stopped" : "why slowed: tracking car " + JunctionWatch.EntityText(car) + " (" + Source + ")";
        }

        /// <summary>
        /// A new reading. Samples of another car (the job's last step before a change of car) and repeats of the same
        /// sequence are ignored. Returns the line to log (reason change, within the rate limit) or null.
        /// </summary>
        public string Observe(WhySample s, double seconds)
        {
            if (Car == 0 || s.Car != Car || s.Seq == m_LastSeq) return null;
            m_LastSeq = s.Seq;
            bool changed = !HasCurrent || !WhySample.SameReason(s, Current);
            Current = s;
            HasCurrent = true;
            if (!changed) return null;
            m_Changes.Add(s);
            if (m_Changes.Count > MaxChanges) m_Changes.RemoveAt(0);
            if (!m_Limiter.Allow(seconds)) { DroppedLines++; return null; }
            long dropped = m_Limiter.TakeDropped();
            return Line(s) + (dropped > 0 ? " (" + dropped.ToString(CultureInfo.InvariantCulture) + " earlier change line(s) dropped by the rate limit)" : "");
        }

        private static string F(float v, string fmt) => float.IsNaN(v) ? "?" : v.ToString(fmt, CultureInfo.InvariantCulture);

        /// <summary>The log line of one reading.</summary>
        public static string Line(WhySample s)
        {
            string extra = float.IsNaN(s.Extra) ? "" : ", extra " + ExtraText(s.Rule, s.Extra);
            return "why slowed: frame " + s.Frame.ToString(CultureInfo.InvariantCulture) + " car " + JunctionWatch.EntityText(s.Car)
                   + " at (" + F(s.X, "0.0") + ", " + F(s.Z, "0.0") + ") speed " + F(s.Speed, "0.0") + " m/s: " + WhyRules.Text(s.Rule)
                   + " [" + WhyRules.Id(s.Rule) + "], limit " + F(s.Limit, "0.00") + " m/s"
                   + ", blocker " + (s.Blocker == 0 ? "none" : JunctionWatch.EntityText(s.Blocker) + " " + WhyRules.KindId(s.BlockerKind)
                       + (s.BlockerKind == WhyKind.None || s.BlockerKind == WhyKind.Gone ? "" : " at (" + F(s.BlockerX, "0.0") + ", " + F(s.BlockerZ, "0.0") + ")"))
                   + ", distance " + F(s.Distance, "0.0") + " m, lane " + JunctionWatch.EntityText(s.Lane) + extra;
        }

        /// <summary>The rule-specific number in words.</summary>
        public static string ExtraText(WhyRule r, float extra)
        {
            if (float.IsNaN(extra)) return "none";
            switch (r)
            {
                case WhyRule.JrBoxLookPastRefused: return "look-past refused: " + WhyRules.RefusalId((LookPastRefusal)(int)extra);
                case WhyRule.JrPeopleWaitSpot: return "reason " + ZebraDriveUp.ReasonId((ZebraDriveUp.Reason)(int)extra);   // JR-T-033
                case WhyRule.JrBoxRoom:
                case WhyRule.JrBoxTurnSpot:
                case WhyRule.JrZebraSection:
                case WhyRule.JrSoftZebra:
                case WhyRule.JrExitBoxRoom: return "room short by " + extra.ToString("0.0", CultureInfo.InvariantCulture) + " m";
                case WhyRule.StockGiveWay:
                case WhyRule.StockGiveWaySlow:
                case WhyRule.StockGiveWaySpace:
                case WhyRule.JrWholeJunctionGap:
                case WhyRule.JrSafetyRecheck: return "priority delta " + extra.ToString("0", CultureInfo.InvariantCulture);
                default: return extra.ToString("0.###", CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// The API answer (WhySlowed): the tracked car, its current reading and the reason changes (newest first) of
        /// <paramref name="historyCar"/> (0 = the tracked car), and the cars with a kept history. notes: lines for the caller.
        /// </summary>
        public string Json(string modVersion, IList<string> notes, long historyCar = 0)
        {
            JsonOut j = BoxApi.Begin(true, modVersion, "why_slowed");
            j.Name("tracking").BeginObject().Prop("car", Car == 0 ? null : KeyText(Car)).Prop("source", Source).EndObject();
            j.Name("current");
            if (HasCurrent) Write(j, Current); else j.Null();
            long hc = historyCar != 0 ? historyCar : Car;
            IReadOnlyList<WhySample> h = HistoryOf(hc);
            j.Prop("historyCar", hc == 0 ? null : KeyText(hc));
            j.Name("changes").BeginArray();
            if (h != null) for (int i = h.Count - 1; i >= 0; i--) Write(j, h[i]);   // newest first
            j.EndArray();
            j.Name("knownCars").BeginArray();
            foreach (long c in KnownCars) j.Value(KeyText(c));
            j.EndArray();
            j.Name("notes").BeginArray();
            if (notes != null) foreach (string n in notes) j.Value(n);
            j.EndArray();
            return j.EndObject().ToString();
        }

        /// <summary>"index:version" (the API's car form).</summary>
        public static string KeyText(long key) => JunctionWatch.KeyIndex(key).ToString(CultureInfo.InvariantCulture) + ":" + JunctionWatch.KeyVersion(key).ToString(CultureInfo.InvariantCulture);

        private static void Write(JsonOut j, WhySample s)
        {
            j.BeginObject()
                .Prop("frame", (double)s.Frame, 0)
                .Prop("rule", WhyRules.Id(s.Rule))
                .Prop("text", WhyRules.Text(s.Rule))
                .Prop("jr", WhyRules.IsJr(s.Rule))
                .Prop("limit", s.Limit, 3)
                .Prop("speed", s.Speed, 3)
                .Prop("distance", s.Distance, 2);
            j.Name("extra").Value(s.Extra, 3);
            j.Prop("extraText", float.IsNaN(s.Extra) ? null : ExtraText(s.Rule, s.Extra));
            j.Name("position").BeginObject().Prop("x", s.X, 2).Prop("z", s.Z, 2).EndObject();
            j.Name("blocker");
            if (s.Blocker == 0) j.Null();
            else
            {
                j.BeginObject().Prop("id", KeyText(s.Blocker)).Prop("kind", WhyRules.KindId(s.BlockerKind));
                j.Name("position");
                if (s.BlockerKind == WhyKind.None || s.BlockerKind == WhyKind.Gone) j.Null();
                else j.BeginObject().Prop("x", s.BlockerX, 2).Prop("z", s.BlockerZ, 2).EndObject();
                j.EndObject();
            }
            j.Prop("lane", s.Lane == 0 ? null : KeyText(s.Lane));
            j.Prop("finalLimit", s.FinalLimit, 3);
            j.Prop("gameBlocker", s.GameBlocker == 0 ? null : KeyText(s.GameBlocker));
            j.Prop("gameBlockerType", JunctionWatch.BlockerTypeName(s.GameBlockerType));
            j.EndObject();
        }

        /// <summary>
        /// Parses the WhySlowed argument. null / "" / {} = read only. {"car":"I:V"} or {"car":{"index":I,"version":V}} = the API
        /// tracks that car (the game's selection still wins while a vehicle is selected); {"car":null} or {"clear":true} =
        /// stop the API tracking; {"history":"I:V"} = read that car's kept history without changing what is tracked.
        /// False with a reason on bad input.
        /// </summary>
        public static bool ParseArgs(string json, out bool set, out long car, out long history, out string error)
        {
            set = false;
            car = 0;
            history = 0;
            error = null;
            if (string.IsNullOrWhiteSpace(json)) return true;
            if (!JunctionRules.Core.Json.TryParse(json, out JsonValue v, out string perr)) { error = "not JSON: " + perr; return false; }
            if (!v.IsObject) { error = "the argument must be a JSON object"; return false; }
            foreach (var m in v.Members)
                if (m.Key != "car" && m.Key != "clear" && m.Key != "history") { error = "unknown field '" + m.Key + "' (use car, clear or history)"; return false; }
            JsonValue clear = v.Get("clear");
            if (clear != null)
            {
                if (clear.Kind != JsonKind.Bool) { error = "clear must be true or false"; return false; }
                if (clear.Bool)
                {
                    if (v.Has("car")) { error = "give car or clear, not both"; return false; }
                    set = true;
                }
            }
            JsonValue h = v.Get("history");
            if (h != null && !ParseCar(h, "history", out history, out error)) return false;
            JsonValue c = v.Get("car");
            if (c == null) return true;
            set = true;
            if (c.Kind == JsonKind.Null) return true;
            return ParseCar(c, "car", out car, out error);
        }

        /// <summary>"I:V" or {"index":I,"version":V} (positive whole numbers) to a key.</summary>
        public static bool ParseCar(JsonValue c, string field, out long key, out string error)
        {
            key = 0;
            error = null;
            int index, version;
            if (c.Kind == JsonKind.String)
            {
                string[] parts = c.String.Split(':');
                if (parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out index)
                    || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out version))
                { error = field + " must be \"index:version\""; return false; }
            }
            else if (c.IsObject)
            {
                JsonValue ji = c.Get("index"), jv = c.Get("version");
                if (ji == null || jv == null || !ji.TryInt(out index) || !jv.TryInt(out version)) { error = field + " needs whole numbers index and version"; return false; }
            }
            else { error = field + " must be \"index:version\" or {index, version}" + (field == "car" ? " or null" : ""); return false; }
            if (index <= 0 || version <= 0) { error = field + " index and version must be positive"; return false; }
            key = JunctionWatch.Key(index, version);
            return true;
        }
    }

    /// <summary>
    /// JR-T-025: main thread state shared by the API and the copy's bridge (the API never names copy types). Tracking rule:
    /// the game's SELECTED vehicle wins; with no vehicle selected, the car the API asked for (until cleared).
    /// </summary>
    public static class WhyHandoff
    {
        /// <summary>The car the API asked to track (0 = none).</summary>
        public static long ApiCar;
        /// <summary>The readout book (the bridge feeds it each copy step). Replaced by <see cref="Reset"/>.</summary>
        public static WhyBook Book { get; private set; } = new WhyBook();
        /// <summary>Copy steps the bridge has run (0 = the Junction Rules car driving has not run: game's own driving or not loaded).</summary>
        public static long Steps;
        /// <summary>The API's step count at its last call (API notes: "no driving step since the last call").</summary>
        public static long StepsAtApiCall;
        /// <summary>The tracked car exists and is a car the navigation job drives (main thread, each copy step).</summary>
        public static bool TrackedExists;
        /// <summary>Bumped by <see cref="Reset"/>; the bridge drops the slot's last sample when it changes (that sample is of the old city).</summary>
        public static int Generation;

        /// <summary>
        /// JR-T-025b (M1): a city load, leaving the city or the copy's release. Entity index:version values are reused across
        /// saves, so nothing of the old city may survive: the API car, the step counts, and the book (current reading, the
        /// histories and the earlier cars) all start fresh. Main thread.
        /// </summary>
        public static void Reset()
        {
            ApiCar = 0;
            Steps = 0;
            StepsAtApiCall = 0;
            TrackedExists = false;
            Book = new WhyBook();
            Generation++;
        }

        /// <summary>Lines for the API caller about the state of the readout.</summary>
        public static List<string> Notes(long stepsAtLastCall, long stepsNow, bool hasCurrent, long car)
        {
            var n = new List<string>();
            if (stepsNow == 0) n.Add("the Junction Rules car driving has not run yet (game's own driving, or no city): no readings");
            else if (stepsNow == stepsAtLastCall) n.Add("no Junction Rules driving step since the last call (game paused, or the game's own driving)");
            if (car == 0) n.Add("nothing tracked: select a vehicle in the game, or call with {\"car\":\"index:version\"}");
            else if (stepsNow > 0 && !TrackedExists) n.Add("the tracked car is not a driving car now (gone, parked or not a car)");
            else if (!hasCurrent) n.Add("no reading yet for the tracked car (the next driving step writes one)");
            return n;
        }

        /// <summary>The car to track and why: the selection (a vehicle) wins, else the API's car.</summary>
        public static long Choose(long selectedVehicle, long apiCar, out string source)
        {
            if (selectedVehicle != 0) { source = "selection"; return selectedVehicle; }
            if (apiCar != 0) { source = "api"; return apiCar; }
            source = "none";
            return 0;
        }
    }
}
