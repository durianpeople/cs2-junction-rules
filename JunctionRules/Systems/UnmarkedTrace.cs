using System.Threading;
using JunctionRules.Core;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace JunctionRules.Systems
{
    /// <summary>
    /// JR-T-014d (G11): the always-on diagnostics buffer (layout: Core/UnmarkedTraceLog.cs UnmarkedTraceLayout), reachable from
    /// Burst jobs through a SharedStatic pointer, so the copy's car job needs no new job field. Allocated once per process on
    /// the main thread (UnmarkedCrossingSystem.OnCreate) and never freed: a job may still hold the pointer when a world is
    /// destroyed (16.5 KB). Job side: Interlocked adds and fixed ring writes only. Before allocation every call is a no-op.
    /// </summary>
    public static unsafe class UnmarkedTrace
    {
        public struct Buffer
        {
            public long* P;
        }

        private struct Key { }

        public static readonly SharedStatic<Buffer> Shared = SharedStatic<Buffer>.GetOrCreate<Buffer, Key>();

        /// <summary>Main thread: allocate (zeroed) once.</summary>
        public static void Ensure()
        {
            if (Shared.Data.P != null) return;
            long bytes = (long)UnmarkedTraceLayout.TotalLongs * sizeof(long);
            long* p = (long*)UnsafeUtility.Malloc(bytes, 16, Allocator.Persistent);
            UnsafeUtility.MemClear(p, bytes);
            Shared.Data.P = p;
        }

        public static bool Ready => Shared.Data.P != null;

        public static void Add(int counter, long value)
        {
            long* p = Shared.Data.P;
            if (p == null) return;
            Interlocked.Add(ref p[counter], value);
        }

        /// <summary>Main thread: one counter (0 before allocation).</summary>
        public static long Read(int index)
        {
            long* p = Shared.Data.P;
            return p == null ? 0 : Interlocked.Read(ref p[index]);
        }

        public static long Frame
        {
            get { long* p = Shared.Data.P; return p == null ? 0 : Interlocked.Read(ref p[UnmarkedTraceLayout.FrameSlot]); }
        }

        /// <summary>Main thread: publish the simulation frame for the job-side records.</summary>
        public static void SetFrame(uint frame)
        {
            long* p = Shared.Data.P;
            if (p != null) Interlocked.Exchange(ref p[UnmarkedTraceLayout.FrameSlot], frame);
        }

        /// <summary>One ring record (stamp cleared first, payload, stamp = sequence + 1 last).</summary>
        public static void Record(int kind, long a, long b, long c, long d, long e, long f)
        {
            long* p = Shared.Data.P;
            if (p == null) return;
            long seq = Interlocked.Increment(ref p[UnmarkedTraceLayout.Cursor]) - 1;
            int s = UnmarkedTraceLayout.SlotBase(seq);
            Interlocked.Exchange(ref p[s], 0L);
            p[s + 1] = kind;
            p[s + 2] = a; p[s + 3] = b; p[s + 4] = c; p[s + 5] = d; p[s + 6] = e; p[s + 7] = f;
            Interlocked.Exchange(ref p[s], seq + 1);
        }

        /// <summary>Main-thread reader for the drain.</summary>
        public struct Reader : ITraceBuffer
        {
            public long Read(int index)
            {
                long* p = Shared.Data.P;
                return p == null ? 0 : Interlocked.Read(ref p[index]);
            }
        }
    }
}
