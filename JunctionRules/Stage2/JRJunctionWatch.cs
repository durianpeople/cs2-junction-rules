using System.Threading;
using Game.Vehicles;
using JunctionRules.Core;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace JunctionRules.Stage2
{
    /// <summary>
    /// JR-T-013b (G4), job side of "watch one junction" (Core/JunctionWatch.cs): a debug log of the holds at one node.
    /// Fields live in JRWholeJunction.cs. Only the shared watch array is written (atomic slot claim, stamp last). No
    /// driving effect: nothing here changes a speed, blocker or lane.
    /// </summary>
    public partial struct JRWholeJunction
    {
        /// <summary>S2-21: the watched node from slot 0 of the shared watch array (0 = off).</summary>
        public void SetWatch(NativeArray<long> watch)
        {
            m_WatchNode = Entity.Null;
            m_WatchSeen = false;
            if (!watch.IsCreated || watch.Length < JunctionWatch.Length) return;
            long key = watch[0];
            if (key == 0) return;
            m_WatchNode = new Entity { Index = JunctionWatch.KeyIndex(key), Version = JunctionWatch.KeyVersion(key) };
        }

        /// <summary>
        /// S2-22 (counting is on while a node is watched): one event for a car that stands at the watched junction (on
        /// one of its lanes, or it checked one of its lanes this step). Which rule bound its final limit is read from the
        /// per-car results the counters use (binding = recorded limit at or under the final one); none = the game's own.
        /// </summary>
        public unsafe void Watch(NativeArray<long> watch, Entity entity, float finalMaxSpeed, float currentSpeed, Entity blocker, BlockerType blockerType)
        {
            if (m_WatchNode == Entity.Null || !watch.IsCreated || watch.Length < JunctionWatch.Length) return;
            if (!(currentSpeed < Stage2Counters.StandingSpeed)) return;
            if (!m_WatchSeen && !OnNode(m_CurrentLane, m_WatchNode) && !OnNode(m_CurrentLane2, m_WatchNode)) return;
            int rule = (int)WatchRule(finalMaxSpeed);
            long* p = (long*)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(watch);
            long seq = Interlocked.Increment(ref p[1]) - 1;
            int s = JunctionWatch.Slot(seq);
            p[s + 1] = JunctionWatch.Key(entity.Index, entity.Version);
            p[s + 2] = JunctionWatch.Key(blocker.Index, blocker.Version);
            p[s + 3] = JunctionWatch.Key(m_CurrentLane.Index, m_CurrentLane.Version);
            p[s + 4] = JunctionWatch.Pack(rule, (int)blockerType, currentSpeed, finalMaxSpeed);
            Interlocked.Exchange(ref p[s], seq + 1);
        }

        private JunctionWatch.Rule WatchRule(float f)
        {
            if (Stage2Counters.IsBinding(m_HasBoxTurnRoom, m_BoxTurnRoomSpeed, f)) return JunctionWatch.Rule.BoxTurnRoom;
            if (Stage2Counters.IsBinding(m_HasBoxTurnWait, m_BoxTurnWaitSpeed, f)) return JunctionWatch.Rule.BoxTurnOncoming;
            if (Stage2Counters.IsBinding(m_HasBoxRoom, m_BoxRoomSpeed, f)) return JunctionWatch.Rule.BoxLine;
            if (Stage2Counters.IsBinding(m_HasEntranceMain, m_EntranceMainSpeed, f)) return JunctionWatch.Rule.EntranceMain;
            if (Stage2Counters.IsBinding(m_HasEntranceLeaving, m_EntranceLeavingSpeed, f)) return JunctionWatch.Rule.EntranceLeaving;
            if (Stage2Counters.IsBinding(m_HasZebraPeople, m_ZebraPeopleSpeed, f)) return JunctionWatch.Rule.ZebraPeople;
            if (Stage2Counters.IsBinding(m_HasZebraRoom, m_ZebraRoomSpeed, f)) return JunctionWatch.Rule.ZebraRoom;
            if (Stage2Counters.IsBinding(m_HasZebraCs, m_ZebraCsSpeed, f)) return JunctionWatch.Rule.ZebraVehicles;
            if (Stage2Counters.IsBinding(m_HasLater, m_LaterSpeed, f)) return JunctionWatch.Rule.WaitLater;
            if (Stage2Counters.IsBinding(m_HasInsideYield, m_InsideYieldSpeed, f)) return JunctionWatch.Rule.InsideGiveWay;
            if (Stage2Counters.IsBinding(m_HasInsideCrossing, m_InsideCrossingSpeed, f)) return JunctionWatch.Rule.InsideCrossing;
            if (Stage2Counters.IsBinding(m_HasSafety, m_SafetySpeed, f)) return JunctionWatch.Rule.Safety;
            if (Stage2Counters.IsBinding(m_HasExitHold, m_ExitHoldSpeed, f)) return JunctionWatch.Rule.Exit;
            return JunctionWatch.Rule.Game;
        }
    }
}
