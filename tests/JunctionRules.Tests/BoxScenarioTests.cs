using System;
using System.Collections.Generic;
using JunctionRules.Core;

namespace JunctionRules.Tests
{
    /// <summary>
    /// 0.4.10 (JR-T-016, owner 17 Nov 2026: "make 0.4.10 robust ... no part of a waiting vehicle inside any painted box; no
    /// new deadlock loops ... a combined Alderwood / Alderbrook scenario test"). A step model of one road lane each, with the
    /// live box windows, driven by the SAME Core decisions as the job (ExitBoxWindows.Chains / MergeGapMetres for the paint,
    /// ExitBands.Chain + HoldTouchMetres for the per-vehicle band, BoxDriving.Role / EnteringEntry, RoomScan with FreeNow,
    /// ExtendPast for a further box or boxed junction inside the reach, BoxDriving.LightStopsInWindow, ExitGiveWay's G15
    /// "an entrant waiting for room in is not present" rule). Vehicle mix: car 4.2 m, van 6.5 m, bus 12 m, artic 16.5 m
    /// (tractor + trailer = the job's whole length). Traffic past the lane end is stop-and-go (the exit opens at random).
    ///
    /// Alderwood (westbound past No. 40, edge 557050, PROBE 19.1 / 20.4): entrance box 34.7..42.7 m and exit box
    /// 45.5..53.5 m (2.8 m apart: one box since G20), a full yard (capacity 3, one-way: in by the entrance drive at 38.7 m,
    /// out by the exit drive at 49.5 m), vans and artics turning in, a boxed junction at the lane end (110..124 m).
    /// Alderbrook (northbound, edge 984373, metres from the south mouth): 843726 exit 6.7..14.7, the 193433 + 843726 joint
    /// box 48.0..65.3, 843724 exit 107.1..115.1, 843722 entrance 151.7..159.7, a signal at the north mouth 168.1 m
    /// (8.4 m past the last box: a bus or artic waiting at red would stand in it).
    /// Checked every step: no STANDING vehicle has any part inside a painted box; at the end (demand off, exit open, lights
    /// still cycling, the yard still turning over) everything has drained: no deadlock.
    /// </summary>
    internal static partial class Program
    {
        private sealed class SimVehicle
        {
            public int Id;
            public float Front, Length, Speed;
            public bool Entrant, Reserved, WaitingToEnter;
            /// <summary>Crossed a box line while the light was green (not amber / red): a later light change can catch it.</summary>
            public bool EnteredOnGreen;
            /// <summary>Crossed a box line during amber / red only because it could no longer stop (review 3 F3: counted apart).</summary>
            public bool EnteredCommittedAmber;
            public float Accel, Decel;
            /// <summary>This step's binding stop was a box hold at HoldLine (the painted line).</summary>
            public bool HeldByBox;
            public float HoldLine;
            public bool CommittedCounted;
            /// <summary>At its last box line decision the vehicle could still stop before the line.</summary>
            public bool LastCanStop = true;
            public bool HeldForLight;
            /// <summary>JR-T-022 sim: top speed (a slow lorry), and a box line whose hold this vehicle ignores (counterfactual run).</summary>
            public float VMax = SimVmax;
            public float IgnoreLine = float.NaN;
            /// <summary>JR-T-022 sim: its last box decision let it go only by the look-past; it entered the current box so.</summary>
            public bool LastLookAdmit, EnteredByLook;
            /// <summary>JR-T-022c sim: a scan object that is one element of an artic (trailer / tractor): Large whatever its length.</summary>
            public bool ArticElement;

            public SimVehicle Clone() => (SimVehicle)MemberwiseClone();
        }

        private sealed class SimLane
        {
            public string Name;
            public float End;                       // lane end (stop line of the junction / the light)
            public float JunctionEnd;               // where the vehicle leaves the model (past the junction)
            public bool JunctionBoxed;
            public bool Light;                      // signal at End
            public List<(float a, float b)> Painted = new List<(float a, float b)>();
            public float Drive = -1f, ExitJoin = -1f; // yard: entrance drive and exit join point (-1 = none)
            public int Phase;                         // light phase (SimPhase)
            public bool Petition;                     // a car of this approach asked for green this step
            public bool RequestFromBoxHold = true;    // 0.4.10 review R1 (block S2-52): a car held before a box for the light asks for it
            public int YardCap, Yard;
            public float NextJunction = -1f;          // JR-T-022: a junction further on where the queue may stop (look-past sight ends there)
            /// <summary>
            /// JR-T-022c sim: the room scan reads an artic (16.5 m) as the game does: two scan objects of the same vehicle
            /// (<see cref="SimElements"/>: trailer first, then its tractor). Driving is unchanged (one body).
            /// </summary>
            public bool TwoElementArtics;
        }

        /// <summary>JR-T-022c sim: an artic's trailer (12 m, rear part) and tractor (5.5 m, front part, 1 m over the trailer's front).</summary>
        private const float SimArticLength = 16.5f, SimTrailerLength = 12f, SimTractorLength = 5.5f;

        /// <summary>
        /// JR-T-022c sim: the scan objects of a vehicle, rear first (path order when it drives away): one, or for an artic on a
        /// lane with <see cref="SimLane.TwoElementArtics"/> its trailer then its tractor, both under the vehicle's id (the job
        /// names a trailer by its controller) and both Large.
        /// </summary>
        private static IEnumerable<SimVehicle> SimElements(SimLane lane, SimVehicle o)
        {
            if (!lane.TwoElementArtics || Math.Abs(o.Length - SimArticLength) > 1e-3f) { yield return o; yield break; }
            SimVehicle trailer = o.Clone();
            trailer.ArticElement = true;
            trailer.Front = o.Front - o.Length + SimTrailerLength; trailer.Length = SimTrailerLength;
            SimVehicle tractor = o.Clone();
            tractor.ArticElement = true;
            tractor.Length = SimTractorLength;
            yield return trailer;
            yield return tractor;
        }

        private const float SimMargin = 1f, SimDt = 0.5f, SimVmax = 8f;

        /// <summary>The highest speed from which the vehicle stops within 'dist' in whole steps (as the game's GetMaxBrakingSpeed with its time step).</summary>
        private static float SimSafeSpeed(SimVehicle v, float dist)
        {
            if (!(dist > 0f)) return 0f;
            return v.Decel * (-SimDt + (float)Math.Sqrt(SimDt * SimDt + 2f * dist / v.Decel));
        }

        /// <summary>The game's can-stop test: braking from the current speed this step reaches a speed that stops within 'dist'.</summary>
        private static bool SimCanStop(SimVehicle v, float dist) => SimSafeSpeed(v, dist) >= v.Speed - v.Decel * SimDt - 1e-4f;

        /// <summary>The paint: the windows chained with the merge gap (one box each chain), as the paint and DrivingBands do.</summary>
        private static List<(float a, float b)> SimPaint(List<(double a, double b)> windows, double gap)
        {
            var w = new List<ExitWindow>();
            foreach (var x in windows) w.Add(new ExitWindow(1, MedianSplit.AllCarriageways, x.a, x.b));
            var r = new List<(float a, float b)>();
            foreach (var box in ExitBoxWindows.Assemble(w, gap, (m, f, t) => true)) r.Add(((float)box.From, (float)box.To));
            r.Sort((x, y) => x.a.CompareTo(y.a));
            return r;
        }

        /// <summary>The boxes this vehicle's hold sees: painted boxes (+ the boxed junction), chained by its own length + margin.</summary>
        private static List<(float b0, float b1)> SimBands(SimLane lane, float length)
        {
            var all = new List<(float, float)>(lane.Painted);
            if (lane.JunctionBoxed) all.Add((lane.End, lane.JunctionEnd));
            all.Sort((x, y) => x.Item1.CompareTo(y.Item1));
            return ExitBands.Chain(all, ExitBands.HoldTouchMetres(length, SimMargin));
        }

        /// <summary>
        /// The light as the game runs it (review R1: TrafficLightSystem.GetNextSignalGroup changes only to a group a car asked
        /// for): green 17 s (0-33), amber 3 s (34-39), red; after at least 20 s of red (the other approach's green) it turns
        /// green only in a step in which a car of this approach asked for it (lane.Petition).
        /// </summary>
        private static int SimPhase(SimLane lane) => lane.Phase;
        private static bool SimRed(SimLane lane, int step) => lane.Light && lane.Phase >= 40;
        /// <summary>The job's LightStopsInWindow: red or amber (Game.Net.LaneSignalType Stop / SafeStop).</summary>
        private static bool SimLightStops(SimLane lane, int step) =>
            lane.Light && BoxDriving.LightStopsInWindow(lane.Phase >= 40 ? WholePathRule.SignalStop : lane.Phase >= 34 ? WholePathRule.SignalSafeStop : 4);

        private static void SimAdvanceLight(SimLane lane)
        {
            if (!lane.Light) return;
            if (lane.Phase < 79) lane.Phase++;
            else if (lane.Petition) lane.Phase = 0;   // red until asked
        }
        /// <summary>The model's road ends this far past the junction; the stop-and-go hold is there (queues stay visible).</summary>
        private const float SimDownstream = 60f;
        /// <summary>Vehicles enter the model this far before the lane start (at full speed), so every box line is approached with room to brake.</summary>
        private const float SimSpawn = -40f;

        /// <summary>
        /// The box room test for a vehicle entering [b0, b1] (0.4.10 rule when 'robust', else 0.4.9: moving credit, no further
        /// box / light in the window): vehicles ahead inside the box take room first; vehicles past it by rear position.
        /// </summary>
        private static bool SimRoom(SimLane lane, List<SimVehicle> vs, SimVehicle me, float b0, float b1, int step, bool robust, bool lookPast = false) =>
            SimRoom(lane, vs, me, b0, b1, step, robust, out _, lookPast);

        /// <summary>
        /// JR-T-022: the game's braking distance of a sim vehicle at its speed (VehicleUtils.GetBrakingDistance with its own
        /// deceleration and the sim step), the credit the job reads for the look-past.
        /// </summary>
        /// <summary>
        /// JR-T-022b sim: the vehicle blocks the view (the job reads the game's SizeClass Large, RoomScan.BlocksView; the sim has
        /// only lengths: the 12 m rigid lorry and the 16.5 m artic are Large, cars 4.2 m and vans 6.5 m are not).
        /// </summary>
        private static bool SimLarge(SimVehicle o) => o.Length >= 12f || o.ArticElement;

        private static float SimBraking(SimVehicle o) => o.Speed > 0.001f ? 0.5f * o.Speed * o.Speed / o.Decel + o.Speed * SimDt : 0f;

        /// <summary>
        /// JR-T-022: <paramref name="legacy"/> = the 1.0.1 calls verbatim (RoomScan.Add / AddQueued without speeds); else the
        /// JR-T-022 calls (speeds, guaranteed travel, FinishLookPast) with RoomScan.LookPast = <paramref name="lookPast"/>.
        /// </summary>
        private static bool SimRoom(SimLane lane, List<SimVehicle> vs, SimVehicle me, float b0, float b1, int step, bool robust, out bool lightBlocked, bool lookPast = false, bool legacy = false)
        {
            bool lb = false;
            RoomScan scan = RoomScan.Begin(me.Length, SimMargin);
            scan.FreeNow = robust;
            if (!legacy) scan.LookPast = lookPast;
            if (scan.LookPast) scan.SightLeft = RoomScan.SightPast(b1 - me.Front);   // JR-T-022: sight from the front bumper
            // JR-T-022: the next junction (the lane end: a light or a junction; a stop point further on) ends the look-past
            if (scan.LookPast && lane.End >= b1) scan.LimitSight(lane.End - b1);
            if (scan.LookPast && lane.NextJunction >= b1) scan.LimitSight(lane.NextJunction - b1);
            var past = new List<SimVehicle>();
            foreach (SimVehicle v in vs)
            {
                if (v == me || v.Front <= me.Front) continue;
                foreach (SimVehicle o in SimElements(lane, v))
                {
                    float rear = o.Front - o.Length;
                    // 0.4.10 ahead-first (the job's AheadFirst): every vehicle ahead not yet past the box goes through it first;
                    // 0.4.9 counted only those already inside it
                    if (rear < b1 && (robust || o.Front > b0))
                    {
                        // 0.4.10 (chains): a STANDING vehicle ahead in the box / before its line blocks (free now); a moving one goes first
                        if (robust && o.Speed < 0.5f) scan.BlockBy(o.Id, 1);
                        else if (legacy) scan.AddQueued(o.Length);
                        else scan.AddQueued(o.Length, o.Speed, o.Id, 1, SimLarge(o));
                    }
                    else if (rear >= b1) past.Add(o);
                }
            }
            // 0.4.10 (chains): a red / amber light is a standing obstacle at its line (the job's FurtherBoxes: RoomScan.Add at the line)
            if (robust && lane.Light && lane.End >= b1 && SimLightStops(lane, step)) past.Add(new SimVehicle { Id = -1, Front = lane.End, Length = 0f, Speed = 0f });
            past.Sort((x, y) => x.Front.CompareTo(y.Front));   // JR-T-022c: an artic's trailer (front 4.5 m behind its tractor's) comes first
            var further = new List<(float b0, float b1)>();
            foreach (var b in SimBands(lane, me.Length)) if (b.b0 > b1 + 1e-3f) further.Add(b);
            var applied = new bool[further.Count];
            void Extensions()
            {
                if (!robust) return;
                bool again = true;
                while (again && !scan.Blocked)
                {
                    again = false;
                    for (int i = 0; i < further.Count; i++)
                    {
                        if (applied[i]) continue;
                        if (BoxDriving.BandInWindow(further[i].b0, further[i].b1, b1, 1e9f, 1f, 0f, scan.Reach, out float pastEnd) || further[i].b0 - b1 < scan.Reach)
                        {
                            applied[i] = true;
                            scan.ExtendPast(further[i].b1 - b1);
                            again = true;
                        }
                    }

                }
            }
            Extensions();
            foreach (SimVehicle o in past)
            {
                if (scan.Blocked) break;
                float credit = o.Speed > 0.01f ? o.Speed * o.Speed / (2f * 4f) + o.Speed * 0.5f : 0f;
                if (legacy || o.Id == -1) scan.Add(o.Front - o.Length - b1, o.Length, credit, o.Id, 1);   // a light's stop line: the old call
                else scan.Add(o.Front - o.Length - b1, o.Length, credit, o.Id, 1, o.Speed, RoomScan.GuaranteedTravel(SimBraking(o), o.Speed, SimDt), SimLarge(o));
                Extensions();
            }
            if (!legacy) scan.FinishLookPast();
            lightBlocked = !scan.HasRoom && scan.BlockerIndex == -1;
            return scan.HasRoom;
        }

        private static (int violations, int residual, bool drained, int served, int entered, int exited, int nose, int enteredNoRoom, int residualCommitted, int noseLight) SimRun(SimLane lane, int seed, bool robust, int steps, int drainSteps,
            bool lookPast = false, bool legacy = false, bool follow = false)
        {
            return SimRun(lane, seed, robust, steps, drainSteps, lookPast, legacy, out _, follow);
        }

        /// <summary>
        /// JR-T-022: the least distance a sim vehicle covers when it brakes now as hard as it can (its deceleration, whole
        /// steps), a lower bound (as RoomScan.GuaranteedTravel of the game's braking distance).
        /// </summary>
        private static float SimMinStop(SimVehicle o) => Math.Max(0f, 0.5f * o.Speed * o.Speed / o.Decel - o.Speed * SimDt);

        /// <summary>JR-T-022: <paramref name="trace"/> = a hash of every room decision and every vehicle position, step by step.</summary>
        private static (int violations, int residual, bool drained, int served, int entered, int exited, int nose, int enteredNoRoom, int residualCommitted, int noseLight) SimRun(SimLane lane, int seed, bool robust, int steps, int drainSteps,
            bool lookPast, bool legacy, out long trace, bool follow = false)
        {
            long h = 1469598103934665603L;
            void Mix(long x) { h ^= x; h *= 1099511628211L; }
            var rnd = new Random(seed);
            var vs = new List<SimVehicle>();
            var yardDwell = new List<int>();
            for (int i = 0; i < lane.Yard; i++) yardDwell.Add(rnd.Next(5, 40));
            float[] lengths = { 4.2f, 4.2f, 4.2f, 6.5f, 6.5f, 12f, 16.5f };
            int nextId = 1, violations = 0, residual = 0, served = 0, entered = 0, exited = 0, nose = 0, enteredNoRoom = 0, residualCommitted = 0, noseLight = 0;
            for (int step = 0; step < steps + drainSteps; step++)
            {
                bool demand = step < steps;
                bool heavy = seed > 12;   // seeds 13-20: a heavy jam (the exit opens 12 % of the time, more demand)
                bool sinkOpen = !demand || rnd.NextDouble() < (heavy ? 0.12 : 0.35);
                vs.Sort((x, y) => y.Front.CompareTo(x.Front));   // front first
                SimVehicle ahead = null;
                foreach (SimVehicle v in vs)
                {
                    // hard limits (physical: the vehicle ahead, the closed exit) and soft ones (stop lines: braking-limited)
                    float hard = sinkOpen ? 1e9f : lane.JunctionEnd + SimDownstream;
                    if (ahead != null) hard = Math.Min(hard, ahead.Front - ahead.Length - 1f);
                    float soft = 1e9f;
                    // JR-T-022 (follow): keep a gap the vehicle can stop in even if the one ahead brakes as hard as it can (the game's
                    // car following counts the leader's braking distance); off = the 0.4.10 model (only the 1 m physical clamp)
                    if (follow && ahead != null) soft = Math.Min(soft, ahead.Front - ahead.Length - 1f + SimMinStop(ahead));
                    // the stock light: red and amber stop a car that can still stop (amber = SafeStop)
                    if (lane.Light && SimLightStops(lane, step) && v.Front <= lane.End + 1e-3f && SimCanStop(v, lane.End - v.Front)) soft = Math.Min(soft, lane.End);
                    v.WaitingToEnter = false;
                    v.HeldByBox = false;
                    v.HeldForLight = false;
                    foreach (var band in SimBands(lane, v.Length))
                    {
                        if (v.Front > band.b0 + 1e-3f) continue;          // inside or past: carry on (Classify Inside)
                        float frontToLine = band.b0 - v.Front;            // the job's FrontToLine (front bumper to the painted line)
                        bool canStop = SimCanStop(v, frontToLine);         // the job's Classify: Check vs CannotStop
                        v.LastCanStop = canStop;
                        bool entering = v.Entrant && lane.Drive > band.b0 && lane.Drive < band.b1;
                        bool lightBlocked = false;
                        bool room = entering ? v.Reserved || lane.Yard + Reserved(vs) < lane.YardCap : SimRoom(lane, vs, v, band.b0, band.b1, step, robust, out lightBlocked, lookPast, legacy);
                        Mix(room ? 1 : 2);
                        if (entering && room && !v.Reserved && canStop) v.Reserved = true;
                        if (!room && canStop)
                        {
                            soft = Math.Min(soft, band.b0 - 0.5f);         // front bumper 0.5 m before the line
                            v.HeldByBox = true;
                            v.HoldLine = band.b0;
                            v.HeldForLight = lightBlocked;
                            v.WaitingToEnter = entering;
                        }
                        else if (!room && !canStop && !v.CommittedCounted)
                        {
                            enteredNoRoom++; v.CommittedCounted = true; if (entering) v.Reserved = true;
                        }
                        else if (room && entering) v.Reserved = true;
                        break;                                                // the next box ahead only
                    }
                    float vNext = Math.Min(v.Speed + v.Accel * SimDt, SimVmax);
                    vNext = Math.Min(vNext, SimSafeSpeed(v, soft - v.Front));
                    vNext = Math.Max(vNext, Math.Max(0f, v.Speed - v.Decel * SimDt));   // cannot brake harder than its deceleration
                    float move = vNext * SimDt;
                    if (move > hard - v.Front) move = Math.Max(0f, hard - v.Front);    // physical: never into the vehicle ahead
                    foreach (var pb in lane.Painted)
                        if (v.Front <= pb.a && v.Front + move > pb.a)
                        {
                            // light-change residual: crossed on green, or during amber / red only because it could no longer stop
                            v.EnteredOnGreen = lane.Light && !SimLightStops(lane, step);
                            v.EnteredCommittedAmber = lane.Light && SimLightStops(lane, step) && !v.LastCanStop;
                            v.CommittedCounted = false;
                        }
                    v.Speed = move / SimDt;
                    v.Front += move;
                    Mix(BitConverter.SingleToInt32Bits(v.Front));
                    ahead = v;
                }
                // turn in / leave the model
                for (int i = vs.Count - 1; i >= 0; i--)
                {
                    SimVehicle v = vs[i];
                    if (v.Entrant && v.Reserved && v.Front >= lane.Drive) { vs.RemoveAt(i); lane.Yard++; yardDwell.Add(rnd.Next(5, 40)); entered++; continue; }
                    if (v.Front > lane.JunctionEnd + SimDownstream + 0.5f) { vs.RemoveAt(i); served++; }
                }
                // the yard's exit (one-way): gives way, room free now past the box it joins into
                if (lane.ExitJoin >= 0f)
                {
                    for (int k = 0; k < yardDwell.Count; k++) yardDwell[k]--;
                    int ready = yardDwell.FindIndex(d => d <= 0);
                    if (ready >= 0)
                    {
                        var me = new SimVehicle { Id = nextId, Front = lane.ExitJoin, Length = 6.5f, Speed = 2f, Accel = 2f, Decel = 4.5f };
                        bool clear = true;
                        foreach (SimVehicle o in vs)
                        {
                            float rear = o.Front - o.Length;
                            if (o.Front > lane.ExitJoin - me.Length - 1f && rear < lane.ExitJoin + 1f) clear = false;          // body on the join
                            else if (o.Speed > 0.1f && o.Front > lane.ExitJoin - 20f && o.Front <= lane.ExitJoin) clear = false; // approaching
                            else if (o.Speed <= 0.1f && !o.WaitingToEnter && o.Front > lane.ExitJoin - ExitGiveWay.PresentMetres && o.Front <= lane.ExitJoin) clear = false; // G7 present; G15: entrants not
                        }
                        if (clear)
                        {
                            foreach (var band in SimBands(lane, me.Length))
                            {
                                if (lane.ExitJoin < band.b0 || lane.ExitJoin > band.b1) continue;
                                bool exitRoom = SimRoom(lane, vs, me, lane.ExitJoin, band.b1, step, robust, out _, lookPast, legacy);
                                Mix(exitRoom ? 3 : 4);
                                if (!exitRoom) clear = false;
                            }
                        }
                        if (clear) { nextId++; vs.Add(me); yardDwell.RemoveAt(ready); lane.Yard--; exited++; }
                    }
                }
                // demand
                if (demand && rnd.NextDouble() < (heavy ? 0.25 : 0.18))
                {
                    float len = lengths[rnd.Next(lengths.Length)];
                    float lastRear = 1e9f;
                    foreach (SimVehicle o in vs) lastRear = Math.Min(lastRear, o.Front - o.Length);
                    if (lastRear > SimSpawn + len + 2f)
                        vs.Add(new SimVehicle
                        {
                            Id = nextId++, Front = SimSpawn, Length = len, Speed = SimVmax, Entrant = lane.Drive >= 0f && len >= 6.5f && rnd.NextDouble() < 0.4,
                            // car / van / bus / artic: acceleration and deceleration (m/s2)
                            Accel = len < 5f ? 2.5f : len < 7f ? 2f : len < 13f ? 1.2f : 1f, Decel = len < 5f ? 5f : len < 7f ? 4.5f : len < 13f ? 3.5f : 3f,
                        });
                }
                // the invariant: no part of a STANDING vehicle in a painted box
                foreach (SimVehicle v in vs)
                {
                    if (v.Speed > 0.01f || v.Front < 0f) continue;
                    foreach (var b in lane.Painted)
                        if (v.Front > b.a + 0.01f && v.Front - v.Length < b.b - 0.01f)
                        {
                            // known residual (PROBE 20.6): the first vehicle at a red light that entered the box on green and
                            // met amber / red before the line (the light's remaining green is not read)
                            bool lightCase = lane.Light && SimLightStops(lane, step) && (v.EnteredOnGreen || v.EnteredCommittedAmber);
                            // the job's nose counter (BoxDriving.NoseIn: front past the box's near line, rear still before it)
                            if (BoxDriving.NoseIn(b.a - v.Front, v.Length)) { if (lightCase) noseLight++; else nose++; }
                            if (lane.Light && SimLightStops(lane, step) && v.EnteredOnGreen) { residual++; break; }
                            if (lightCase) { residualCommitted++; break; }
                            violations++;
                            break;
                        }
                    if (lane.JunctionBoxed && v.Front > lane.End + 0.01f && v.Front - v.Length < lane.JunctionEnd - 0.01f) violations++;
                }
                // the light: who asks for green. The game: a car whose look-ahead reaches the light's lane (not one held
                // earlier: its iteration ends at its hold). 0.4.10 S2-52: a car held before a box FOR the light asks too.
                lane.Petition = false;
                foreach (SimVehicle v in vs)
                {
                    if (v.HeldForLight) { if (lane.RequestFromBoxHold) lane.Petition = true; continue; }
                    if (!v.HeldByBox && v.Front <= lane.End + 1e-3f && v.Front > lane.End - 40f) lane.Petition = true;
                }
                SimAdvanceLight(lane);
            }
            bool drained = vs.Count == 0 && lane.Yard == 0 && yardDwell.Count == 0;
            trace = h;
            return (violations, residual, drained, served, entered, exited, nose, enteredNoRoom, residualCommitted, noseLight);
        }

        private static int Reserved(List<SimVehicle> vs)
        {
            int n = 0;
            foreach (SimVehicle v in vs) if (v.Reserved) n++;
            return n;
        }

        private static SimLane Alderwood(double gap) => new SimLane
        {
            Name = "Alderwood", End = 110f, JunctionEnd = 124f, JunctionBoxed = true,
            Painted = SimPaint(new List<(double, double)> { (34.7, 42.7), (45.5, 53.5) }, gap),
            Drive = 38.7f, ExitJoin = 49.5f, YardCap = 3, Yard = 3,
        };

        private static SimLane Alderbrook(double gap) => new SimLane
        {
            Name = "Alderbrook", End = 168.1f, JunctionEnd = 182f, Light = true,
            Painted = SimPaint(new List<(double, double)> { (6.7, 14.7), (48.0, 56.0), (57.3, 65.3), (107.1, 115.1), (151.7, 159.7) }, gap),
        };

        /// <summary>0.4.10 (owner, chains of boxes): boxes with gaps of 2.8 m (painted as one), 6.1 m, 12 m and 25 m, then a boxed junction.</summary>
        private static SimLane Chained(double gap) => new SimLane
        {
            Name = "Chained", End = 130f, JunctionEnd = 144f, JunctionBoxed = true,
            Painted = SimPaint(new List<(double, double)> { (10, 18), (20.8, 28.8), (34.9, 42.9), (54.9, 62.9), (87.9, 95.9) }, gap),
        };

        private static void BoxScenarioTests()
        {
            Check(Alderwood(ExitBands.MergeGapMetres).Painted.Count == 1 && Alderwood(2.0).Painted.Count == 2, "paint: No. 40's boxes are one box at 6 m (G20), two at 0.4.9's 2 m");
            Check(Chained(ExitBands.MergeGapMetres).Painted.Count == 4, "paint: the chained road's 2.8 m gap is one box, the 6 / 12 / 25 m gaps stay");
            Check(Alderbrook(ExitBands.MergeGapMetres).Painted.Count == 4, "paint: Alderbrook's 193433 + 843726 boxes one box (110.0..127.3 m along = 48.0..65.3 m here)");
            int totalViol = 0, oldViol = 0, residual = 0, oldResidual = 0, served = 0, entered = 0, exited = 0, nose = 0, noRoom = 0, oldNoRoom = 0;
            int resCommitted = 0, oldResCommitted = 0, noseLight = 0, oldNose = 0;
            bool allDrained = true;
            for (int seed = 1; seed <= 20; seed++)
            {
                foreach (int which in new[] { 0, 1, 2 })
                {
                    bool wood = which == 0;
                    SimLane Make(double g) => which == 0 ? Alderwood(g) : which == 1 ? Alderbrook(g) : Chained(g);
                    var r = SimRun(Make(ExitBands.MergeGapMetres), seed, true, 2400, 2400);
                    totalViol += r.violations;
                    residual += r.residual;
                    nose += r.nose;
                    resCommitted += r.residualCommitted;
                    noseLight += r.noseLight;
                    noRoom += r.enteredNoRoom;
                    allDrained &= r.drained;
                    served += r.served; entered += r.entered; exited += r.exited;
                    if (!r.drained || r.violations > 0) Console.WriteLine($"  scenario {Make(6).Name} seed {seed}: violations {r.violations}, drained {r.drained}");
                    var o = SimRun(Make(2.0), seed, false, 2400, 2400);
                    oldViol += o.violations;
                    oldResidual += o.residual;
                    oldResCommitted += o.residualCommitted;
                    oldNose += o.nose + o.noseLight;
                    oldNoRoom += o.enteredNoRoom;
                }
            }
            Console.WriteLine($"  box scenario: 0.4.10 rules: {served} vehicles through, {entered} turned into No. 40, {exited} left it; standing-in-box vehicle-steps: "
                + $"{totalViol} not caused by a light change, {residual} entered on green then stopped by amber / red, {resCommitted} committed into the box during amber / red (could not stop); "
                + $"nose in a box while standing: {nose} not caused by a light change, {noseLight} light change; committed at a line with less room than needed (could not stop) {noRoom}; "
                + $"0.4.9 rules: standing {oldViol} + {oldResidual} + {oldResCommitted}, nose {oldNose}, committed {oldNoRoom}");
            Check(nose == 0, $"owner nose rule: no standing vehicle has its nose in a box (front past the line, rear before it) except where a light changed after it entered ({nose})");
            Check(served > 1000 && entered > 50 && exited > 50, "the scenario moves traffic (through, into and out of the yard)");
            Check(totalViol == 0, $"0.4.10: no standing vehicle has any part inside a painted box (car, van, bus, artic; through, entering, leaving; stop-and-go exit; red light) ({totalViol})");
            Check(allDrained, "0.4.10: no deadlock: with the demand off everything drains (lane empty, No. 40's yard turned over and empty)");
            Check(residual + resCommitted < oldResidual + oldResCommitted + oldViol, $"the light-change residual (first vehicle at a red line, entered on green) is rare next to 0.4.9 ({residual})");
            // review R1: without the light request of a car held before a box for the light, a quiet approach starves
            int starvedRuns = 0, requestRuns = 0;
            for (int seed = 1; seed <= 20; seed++)
            {
                SimLane noRequest = Alderbrook(ExitBands.MergeGapMetres);
                noRequest.RequestFromBoxHold = false;
                if (!SimRun(noRequest, seed, true, 2400, 2400).drained) starvedRuns++;
                if (SimRun(Alderbrook(ExitBands.MergeGapMetres), seed, true, 2400, 2400).drained) requestRuns++;
            }
            Console.WriteLine($"  light request (review R1): without it {starvedRuns} of 20 Alderbrook runs never drain (the light is never asked for green); with it {requestRuns} of 20 drain");
            Check(starvedRuns > 0, "review R1 reproduced: a car held before a box for a red light never asks for green (its look-ahead ends at the box): the approach starves");
            Check(requestRuns == 20, "0.4.10 S2-52: the held car asks the light for green: every run drains");
            Check(oldViol > 0, $"the same scenes under 0.4.9's rules (moving credit, 2 m paint merge, no light / further box in the window) leave vehicles standing in boxes ({oldViol})");
        }

        private static void BoxWindowUnitTests()
        {
            // BandInWindow: a later lane's band [0.2, 0.4] of a 50 m lane entered at 0, the path entering it 3 m into the window
            Check(BoxDriving.BandInWindow(0.2f, 0.4f, 0f, 1f, 50f, 3f, 14f, out float pe) && Math.Abs(pe - 23f) < 1e-4f, "a further box starting inside the reach: room needed past its end (23 m)");
            Check(!BoxDriving.BandInWindow(0.4f, 0.6f, 0f, 1f, 50f, 3f, 14f, out _), "a further box starting beyond the reach: ignored");
            Check(!BoxDriving.BandInWindow(0.0f, 0.1f, 0.2f, 1f, 50f, 0f, 14f, out _), "a box behind where the path enters the lane: ignored");
            Check(BoxDriving.BandInWindow(0.0f, 0.3f, 0.1f, 1f, 50f, 0f, 14f, out float pe2) && Math.Abs(pe2 - 10f) < 1e-4f, "the path enters the lane inside a box: room past its end");
            Check(BoxDriving.LightStopsInWindow(WholePathRule.SignalStop) && BoxDriving.LightStopsInWindow(WholePathRule.SignalSafeStop) && !BoxDriving.LightStopsInWindow(4) && !BoxDriving.LightStopsInWindow(0) && !BoxDriving.LightStopsInWindow(3),
                "red and amber inside the window stop the car there (no room now); green, yield-green and no signal do not");
            RoomScan r = RoomScan.Begin(16.5f, 1f);
            r.FreeNow = true;
            r.AddQueued(4.2f);
            Check(Math.Abs(r.Reach - (17.5f + 5.2f)) < 1e-4f, "reach = room needed + vehicles queued in the box ahead");
            r.Add(30f, 4.2f, 0f, 9, 1);
            Check(Math.Abs(r.Reach - (17.5f + 5.2f)) < 1e-4f, "chains fix: a vehicle PAST the box does not widen the reach (the car stops behind it; it only moves on)");
            RoomScan lightScan = RoomScan.Begin(4.2f, 1f);
            lightScan.FreeNow = true;
            lightScan.Add(2f, 4.2f, 0f, 5, 1);    // a car moving towards the red light, rear 2 m past the box... blocks anyway (2 < 5.2)
            RoomScan lightScan2 = RoomScan.Begin(4.2f, 1f);
            lightScan2.FreeNow = true;
            lightScan2.Add(6f, 4.2f, 0f, 5, 1);   // rear 6 m past the box: room for itself...
            lightScan2.Add(8.4f, 0f, 0f, 0, 0);   // ...but the red light 8.4 m past the box: that car queues there: 8.4 - 5.2 = 3.2 < 5.2
            Check(!lightScan.HasRoom && !lightScan2.HasRoom && lightScan2.BlockerIndex == 0, "a red / amber light is a standing obstacle at its line after the vehicles in front of it (Alderbrook 843722: 8.4 m)");
            // review F2: a box over the half-way point of a two-piece edge: piece 1's band ends at its lane end; on the next
            // lane (piece 2, on the path) the rest of the painted window starts at 0: BandInWindow extends the room past it
            Check(LaneWindow.FromOffsets(0.9f, 1f, 80f, false, out float p0, out float p1) && p1 == 1f, "piece 1: the box clipped at its lane end");
            Check(BoxDriving.BandInWindow(0f, 0.1f, 0f, 1f, 80f, 0f, 5.4f, out float over) && Math.Abs(over - 8f) < 1e-4f, "piece 2: the rest of the box (8 m) lies in the window: room needed past the PAINTED end, not the piece end");
            // ExitBoxWindows.Assemble (review F5): joint boxes the paint accepts, singles otherwise
            uint all = MedianSplit.AllCarriageways;
            var w = new List<ExitWindow> { new ExitWindow(1, all, 0, 8), new ExitWindow(1, all, 10, 18), new ExitWindow(1, all, 40, 48), new ExitWindow(2, 0x1, 0, 8), new ExitWindow(2, 0x2, 3, 11) };
            var acc = ExitBoxWindows.Assemble(w, ExitBands.MergeGapMetres, (m, f, t) => true);
            Check(acc.Count == 4 && acc[0].Members.Count == 2 && acc[0].From == 0 && acc[0].To == 18, "assemble: 2 m apart = one box; 22 m apart and across a median = their own");
            var refused = ExitBoxWindows.Assemble(w, ExitBands.MergeGapMetres, (m, f, t) => false);
            Check(refused.Count == 5, "assemble: a refused joint window (marking / mouth) keeps every box its own");

            // job wiring (source): the same rules as the model above
            string root = RepoRoot();
            string wj = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "JunctionRules", "Stage2", "JRWholeJunction.cs"));
            string it = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "JunctionRules", "Vanilla", "JRCarLaneSpeedIterator.cs"));
            string nav = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "JunctionRules", "Vanilla", "JRCarNavigationSystem.cs"));
            Check(wj.Contains("if (freeNow && !inBox) FurtherBoxes(ref scan, nav, navInverse, navCurve, read, ref carLanes, ref curves);")
                && wj.Contains("BoxDriving.LightStopsInWindow((int)sig.m_Signal)") && wj.Contains("scan.ExtendPast(read + span);") && wj.Contains("scan.ExtendPast(pastEnd);"),
                "job: box room windows extend past a further box / boxed junction and stop at a red / amber light (later path lanes)");
            Check(wj.Contains("if (freeNow && m_BoxEntrances && m_BoxEdges != null && m_EdgeLaneData.HasComponent(lane)") && wj.Contains("BoxDriving.BandInWindow(sameBands[i].x, sameBands[i].y, fromDriving, 1f, length, 0f, scan.Reach, out float pastEnd)"),
                "job: a further box on the same lane inside the reach");
            int emergency = 0, at = 0;
            while ((at = it.IndexOf("if (m_JR.m_BoxRule && !JunctionRules.Core.ExitGiveWay.IsEmergency(m_Priority))", at, StringComparison.Ordinal)) >= 0) { emergency++; at++; }
            Check(emergency == 3, "on-duty emergency vehicles pass boxes (S2-28, S2-28b, S2-37), as at exits");
            Check(nav.Contains("ref m_JRBoxEdges, m_LaneSignalData);"), "the job gets the lane signals for the light-in-window rule");

            // ---- owner nose rule (17 Nov): "if the car can't clear the whole thing, the car should not enter the box"
            // the game's distance at a lane point is from the FRONT BUMPER (copy IterateFirstLane: num2 = m_Distance - bounds.max.z)
            Check(it.Contains("float num = -m_PrefabObjectGeometry.m_Bounds.max.z;") && it.Contains("float num2 = m_Distance + num + m_DistanceOffset;"),
                "the box hold's distance is measured from the front bumper (tractor front for an artic): no nose overhang from the vehicle's position offset");
            Check(BoxDriving.FrontToLine(-0.9f, 0.9f) == 0f && !BoxDriving.NosePast(0f) && !BoxDriving.NosePast(-0.05f) && BoxDriving.NosePast(-0.2f), "front to line = wait + margin; past by more than 0.1 m = nose past");
            // property: a car the box rule can HOLD (Classify = Check) can stop with its front at or before the line; a car
            // whose front is past the line is Inside (never held there, so 'held with the nose past' cannot arise from a hold)
            var rr = new Random(1717);
            bool okCheck = true, okInside = true;
            for (int i = 0; i < 20000; i++)
            {
                float margin = 0.4f + (float)rr.NextDouble() * 0.5f, wait = (float)rr.NextDouble() * 40f - 5f, braking = (float)rr.NextDouble() * 30f;
                BoxDriving.Approach ap = BoxDriving.Classify(false, wait, margin, braking);
                float ftl = BoxDriving.FrontToLine(wait, margin);
                if (ap == BoxDriving.Approach.Check && !(braking <= ftl + 1e-4f)) okCheck = false;
                if (ftl < 0f && ap != BoxDriving.Approach.Inside) okInside = false;
            }
            Check(okCheck, "every held car (Check) can stop within its distance to the line: its front bumper stops at or before the painted line (20000 random approaches)");
            Check(okInside, "a car whose front is past the line is never held by the box (it is inside): the nose counter therefore measures standing cars (BoxDriving.NoseIn), not holds");
            Check(BoxDriving.NoseIn(-0.5f, 4.2f) && !BoxDriving.NoseIn(-5f, 4.2f) && !BoxDriving.NoseIn(0.2f, 4.2f) && !BoxDriving.NoseIn(-0.05f, 4.2f), "NoseIn: front past the line, rear before it (not a car wholly in, not before the line)");
            Check(BoxDriving.BodyInside(-0.5f, 7.5f, 4.2f) && BoxDriving.BodyInside(-12f, -4f, 4.2f) && !BoxDriving.BodyInside(0.3f, 8.3f, 4.2f) && !BoxDriving.BodyInside(-14f, -6f, 4.2f),
                "standing-inside probe: nose in, wholly in, front at the line (outside), rear past the far edge (outside)");
            string yb2 = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "JunctionRules", "Stage2", "JRYellowBox.cs"));
            Check(yb2.Contains("if (BoxDriving.NoseIn(fl, m_VehicleLength) && !m_BoxSameExitStanding") && yb2.Contains("m_BoxStoodInside = true;") && yb2.Contains("                            m_BoxNose = true;") && yb2.Contains("if (BoxDriving.NoseIn(f0, m_VehicleLength))   // review 3 F1")
                && wj.Contains("if (m_BoxNose && !turnSpot && currentSpeed < Stage2Counters.StandingSpeed)") && wj.Contains("Add(counters, Stage2Counters.BoxNosePast, 1);   // owner nose rule: must be 0")
                && wj.Contains("|| Stage2Counters.IsStandingOn(m_HasBoxTurnRoom, m_BoxTurnRoomSpeed, finalMaxSpeed, currentSpeed);")
                && wj.Contains("if (m_BoxStoodInside && !turnSpot && currentSpeed < Stage2Counters.StandingSpeed) Add(counters, Stage2Counters.BoxStoodInside, 1);"),
                "counters 'standing with the nose in a box' (must be 0) and 'standing inside a box' measure standing cars at junction and exit / entrance boxes; the right-turn spot (oncoming gap or exit room) is excluded");
            // review R1: the light request
            Check(nav.Contains("// JR-STAGE2 BEGIN S2-52:") && nav.Contains("laneIterator.m_JR.LightPetition(laneIterator.m_MaxSpeed, out Entity jrPetitionLane)")
                && nav.Contains("m_LaneSignals.Enqueue(new CarNavigationHelpers.LaneSignal(entity, jrPetitionLane, priority));") && wj.Contains("m_LastLightLane = nav.m_Lane;"),
                "S2-52: a car held before a box for a light asks that light for green (the game's own LaneSignal request)");
        }
    }
}
