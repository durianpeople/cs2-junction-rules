using Game.Prefabs;
using Game.Vehicles;
using JunctionRules.Core;
using Unity.Entities;

namespace JunctionRules.Stage2
{
    /// <summary>
    /// 0.2.2 (JR-T-002d): the whole vehicle's length (controller + trailers) for mid-junction waiting spots, computed once
    /// per car and navigation update by the copy's navigation job (JR-STAGE2 block S2-21, as a local of UpdateNavigationTarget:
    /// never a job-struct field, parallel workers share it) and handed to the car's iterator (JRWholeJunction.m_VehicleLength). Only stock data: ObjectGeometryData bounds and the CarTractorData /
    /// CarTrailerData attach points, chained exactly as the game places trailers (Core/VehicleLength.cs has the decompile
    /// references). Burst-compatible: lookups and a buffer read only, no managed allocations.
    /// </summary>
    public static class JRVehicleLength
    {
        /// <summary>
        /// layout = the controller's LayoutElement buffer (element 0 = the controller itself, as the game's UpdateTrailers
        /// assumes); controller = its prefab geometry. Without trailers (length &lt;= 1) this is the controller's bounds
        /// length, the 0.2.1 value. A piece with no tractor/trailer/geometry data stops the chain there (the game itself
        /// always has them for a trailer; this only keeps a broken prefab from throwing).
        /// </summary>
        public static float WithTrailers(ObjectGeometryData controller, DynamicBuffer<LayoutElement> layout,
            ref ComponentLookup<PrefabRef> prefabRefs, ref ComponentLookup<ObjectGeometryData> geometries,
            ref ComponentLookup<CarTractorData> tractors, ref ComponentLookup<CarTrailerData> trailers)
        {
            CombinedLength c = CombinedLength.Of(controller.m_Bounds.min.z, controller.m_Bounds.max.z);
            for (int i = 1; i < layout.Length; i++)
            {
                if (!prefabRefs.TryGetComponent(layout[i - 1].m_Vehicle, out PrefabRef pullerRef)
                    || !tractors.TryGetComponent(pullerRef.m_Prefab, out CarTractorData tractor)
                    || !prefabRefs.TryGetComponent(layout[i].m_Vehicle, out PrefabRef trailerRef)
                    || !trailers.TryGetComponent(trailerRef.m_Prefab, out CarTrailerData trailer)
                    || !geometries.TryGetComponent(trailerRef.m_Prefab, out ObjectGeometryData g))
                {
                    break;
                }
                c.AddTrailer(tractor.m_AttachPosition.z, trailer.m_AttachPosition.z, g.m_Bounds.min.z, g.m_Bounds.max.z);
            }
            return c.Length;
        }
    }
}
