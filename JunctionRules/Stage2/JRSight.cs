using Colossal.Collections;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Prefabs;
using JunctionRules.Core;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace JunctionRules.Stage2
{
    /// <summary>
    /// JR-T-022d (owner ruling 2, 19 Nov 2026: "building = blocked"; "the building is a box and higher than the car"; "just
    /// calculate the straight-line geometry"): job side of the look-past's building test. No precomputed cuts, no eye heights,
    /// no stored data: when a box room test is about to give room thanks to the look-past (RoomScan.FinishSight), each target
    /// it relies on (every vehicle it credited, the end of the room relied on) is placed on the car's path (the lane spans the
    /// scan read, <see cref="SightSpan"/>) and the 2D segment from the driver (Eye) to it is tested against the footprints of
    /// the buildings near it: the game's static object search tree (the navigation job's own m_StaticObjectSearchTree, whose
    /// reader dependency the stock system already handles: GetStaticSearchTree(readOnly) before scheduling,
    /// AddStaticSearchTreeReader after), only tree cells meeting the segment's bounding box, only entities with a Building
    /// component; footprint = the prefab's ObjectGeometryData bounds (x / z) placed by the building's Transform: the LOT box
    /// for ordinary buildings, the mesh bounds for standing ones (BuildingInitializeSystem). A footprint that contains the eye
    /// does not block (JR-T-022e, review F2: a car leaving its own building or in a car park lot stands inside its lot box);
    /// Core SightLine.SightBlocked. Tree cells and items are pruned by the segment itself, not only its bounding box
    /// (SightLine.SegmentHitsAabb, review F5). With the driver's position known this test replaces the next-junction cut,
    /// which lives in FurtherBoxes (kept only without the eye). Fields live in JRWholeJunction.cs; set by block S2-26
    /// (SetSight). Burst-safe: pointers to the job's fields (as m_BoxEdges: no copy per car), struct iterators, no allocations.
    /// </summary>
    public partial struct JRWholeJunction
    {
        /// <summary>JR-T-022d: the most lane spans a look-past room scan reads (the box lane + MaxLookLanes path lanes; JR-T-029, was MaxRoomLanes).</summary>
        public const int MaxSightSpans = 1 + MaxLookLanes;

        /// <summary>
        /// JR-T-022d (block S2-26, only with the box rule): the navigation job's static object search tree, Transform and
        /// Building lookups, by reference (pointers to the job's own fields: valid while its Execute runs).
        /// </summary>
        public unsafe void SetSight(ref NativeQuadTree<Entity, QuadTreeBoundsXZ> staticTree, ref ComponentLookup<Transform> transforms,
            ref ComponentLookup<Game.Buildings.Building> buildings)
        {
            if (!m_BoxLookPast) return;
            m_SightTree = (NativeQuadTree<Entity, QuadTreeBoundsXZ>*)UnsafeUtility.AddressOf(ref staticTree);
            m_SightTransforms = (ComponentLookup<Transform>*)UnsafeUtility.AddressOf(ref transforms);
            m_SightBuildings = (ComponentLookup<Game.Buildings.Building>*)UnsafeUtility.AddressOf(ref buildings);
            m_SightLookups = true;
        }

        /// <summary>JR-T-022d: one lane span the room scan read: its curve, the span's curve offsets and where it starts past the window start.</summary>
        public struct SightSpan
        {
            public Bezier4x3 Curve;
            public float TFrom, TTo, Length, Base;
        }

        /// <summary>JR-T-022d: remember a lane span read by the look-past scan (for placing the sight targets).</summary>
        private static void NoteSpan(ref FixedList512Bytes<SightSpan> spans, Curve c, float tFrom, float tTo, float length, float baseDistance)
        {
            if (spans.Length >= spans.Capacity) return;
            spans.Add(new SightSpan { Curve = c.m_Bezier, TFrom = tFrom, TTo = tTo, Length = length, Base = baseDistance });
        }

        /// <summary>
        /// JR-T-022d: RoomScan.FinishSight's test: the point of the car's path <c>pathMetres</c> past the window start (placed
        /// on the spans read; not on them = not seen) is seen from the eye when no building footprint crosses the straight
        /// line to it (a footprint containing the eye does not count).
        /// </summary>
        private unsafe struct BuildingSight : ISightTest
        {
            public float2 Eye;
            public FixedList512Bytes<SightSpan> Spans;
            public NativeQuadTree<Entity, QuadTreeBoundsXZ>* Tree;
            public ComponentLookup<Transform>* Transforms;
            public ComponentLookup<Game.Buildings.Building>* Buildings;
            public ComponentLookup<PrefabRef>* PrefabRefs;
            public ComponentLookup<ObjectGeometryData>* Geometries;
            public int Queries;

            public bool Seen(float pathMetres)
            {
                if (!Place(pathMetres, out float2 target)) return false;
                Queries++;
                var it = new BuildingOnLine
                {
                    A = Eye, B = target,
                    Box = new Bounds2(math.min(Eye, target), math.max(Eye, target)),
                    Transforms = Transforms, Buildings = Buildings, PrefabRefs = PrefabRefs, Geometries = Geometries,
                };
                Tree->Iterate(ref it);
                return !it.Hit;
            }

            private bool Place(float d, out float2 p)
            {
                p = default;
                // review (JR-T-022d): a vehicle straddling the window start has its rear before it (negative): the sight line goes to
                // the start of the first span (it is right in front of the driver), not "off the path read"
                if (d < 0f) d = 0f;
                for (int i = 0; i < Spans.Length; i++)
                {
                    SightSpan s = Spans[i];
                    float span = math.abs(s.TTo - s.TFrom) * s.Length;
                    if (d < s.Base - 0.01f || d > s.Base + span + 0.01f) continue;
                    float t = s.Length > 0.001f ? s.TFrom + math.sign(s.TTo - s.TFrom) * (d - s.Base) / s.Length : s.TFrom;
                    t = math.clamp(t, math.min(s.TFrom, s.TTo), math.max(s.TFrom, s.TTo));
                    Bezier4x3 b = s.Curve;
                    SightLine.PointAt(b.a.x, b.a.z, b.b.x, b.b.z, b.c.x, b.c.z, b.d.x, b.d.z, t, out float x, out float z);
                    p = new float2(x, z);
                    return true;
                }
                return false;
            }
        }

        /// <summary>JR-T-022d: the search tree walk for one sight line: stops at the first building whose footprint it crosses.</summary>
        private unsafe struct BuildingOnLine : INativeQuadTreeIterator<Entity, QuadTreeBoundsXZ>, IUnsafeQuadTreeIterator<Entity, QuadTreeBoundsXZ>
        {
            public float2 A, B;
            public Bounds2 Box;
            public bool Hit;
            public ComponentLookup<Transform>* Transforms;
            public ComponentLookup<Game.Buildings.Building>* Buildings;
            public ComponentLookup<PrefabRef>* PrefabRefs;
            public ComponentLookup<ObjectGeometryData>* Geometries;

            /// <summary>JR-T-022e (review F5): float slack on the tree bounds before the segment test (they hold the footprint).</summary>
            public const float Pad = 0.05f;

            public bool Intersect(QuadTreeBoundsXZ bounds) => !Hit && OnLine(bounds);

            public void Iterate(QuadTreeBoundsXZ bounds, Entity item)
            {
                if (Hit || !OnLine(bounds) || !Buildings->HasComponent(item)) return;
                if (!Transforms->TryGetComponent(item, out Transform tr) || !PrefabRefs->TryGetComponent(item, out PrefabRef pr)
                    || !Geometries->TryGetComponent(pr.m_Prefab, out ObjectGeometryData g)) return;
                float3 f = math.mul(tr.m_Rotation, new float3(0f, 0f, 1f));
                if (SightLine.SightBlocked(A.x, A.y, B.x, B.y, tr.m_Position.x, tr.m_Position.z, f.x, f.z,
                    g.m_Bounds.min.x, g.m_Bounds.max.x, g.m_Bounds.min.z, g.m_Bounds.max.z)) Hit = true;
            }

            /// <summary>JR-T-022e (review F5): the line's bounding box first (cheap), then the segment itself vs the cell / item box.</summary>
            private bool OnLine(QuadTreeBoundsXZ bounds)
            {
                Bounds3 b = bounds.m_Bounds;
                return MathUtils.Intersect(b.xz, Box)
                    && SightLine.SegmentHitsAabb(A.x, A.y, B.x, B.y, b.min.x - Pad, b.max.x + Pad, b.min.z - Pad, b.max.z + Pad);
            }
        }

        /// <summary>
        /// JR-T-022d: after FinishLookPast, when the look-past is about to give the room: the building test of its targets
        /// (RoomScan.FinishSight). Nothing to do without the driver's position or the lookups (the sight then keeps the
        /// next-junction cut, FurtherBoxes).
        /// </summary>
        private unsafe void BuildingSightCheck(ref RoomScan scan, bool hasEye, float2 eye, ref FixedList512Bytes<SightSpan> spans,
            ref ComponentLookup<PrefabRef> prefabRefs, ref ComponentLookup<ObjectGeometryData> geometries)
        {
            if (!hasEye || !m_SightLookups || !scan.AdmittedByLookPast) return;
            var sight = new BuildingSight
            {
                Eye = eye, Spans = spans, Tree = m_SightTree, Transforms = m_SightTransforms, Buildings = m_SightBuildings,
                PrefabRefs = (ComponentLookup<PrefabRef>*)UnsafeUtility.AddressOf(ref prefabRefs),
                Geometries = (ComponentLookup<ObjectGeometryData>*)UnsafeUtility.AddressOf(ref geometries),
            };
            scan.FinishSight(ref sight);
        }
    }
}
