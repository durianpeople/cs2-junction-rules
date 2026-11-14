namespace JunctionRules.Core
{
    /// <summary>
    /// 0.2.2 (JR-T-002d, owner ruling 14 Nov 2026): the length a mid-junction waiting spot must fit is the whole vehicle,
    /// trailers included. Pure arithmetic on stock prefab data, game-independent and unit-tested; the job side
    /// (Stage2/JRVehicleLength.cs) feeds it ObjectGeometryData bounds and the CarTractorData / CarTrailerData attach points.
    ///
    /// How the game places a trailer (decompile, Game.dll 1.6.4f1): the trailer's attach point is put exactly on the
    /// tractor's attach point, both in the prefab's own frame (z = forward):
    ///   trailer position = tractor position + rotate(tractor rotation, CarTractorData.m_AttachPosition)
    ///                                       - rotate(trailer rotation, CarTrailerData.m_AttachPosition)
    /// (Game.Simulation.CarTrailerMoveSystem Locked/Free/Unspawned, Game.Vehicles.InitializeSystem 323-324,
    /// Game.Rendering.ObjectInterpolateSystem 1754-1768). A trailer that pulls another trailer has CarTractorData itself
    /// (the chain moves on to it: CarTrailerMoveSystem / InitializeSystem 335-338). Attach points come from the prefab:
    /// tractor z = bounds.min.z + CarTractor.m_AttachOffset.z, trailer z = bounds.max.z - CarTrailerPrefab.m_AttachOffset.z
    /// (Game.Prefabs.VehicleInitializeSystem 406-407 / 421-422). So the hitch spacing is entirely in the two stock attach
    /// points; no constant is added here.
    ///
    /// Length = the straight-line extent of all pieces along the controller's axis (front-most front to rear-most rear),
    /// i.e. the combination standing straight, which is how it waits at a spot. A semi-trailer whose front reaches over the
    /// tractor's rear is counted once (extent, not a plain sum). Without trailers the result is exactly the controller's
    /// bounds.max.z - bounds.min.z (the 0.2.1 car length).
    /// </summary>
    public struct CombinedLength
    {
        /// <summary>Front-most z of all pieces so far (controller frame).</summary>
        public float Front;
        /// <summary>Rear-most z of all pieces so far (controller frame).</summary>
        public float Rear;
        /// <summary>z of the last added piece's origin in the controller frame (0 for the controller).</summary>
        public float Origin;

        /// <summary>Starts with the controller (layout element 0): its prefab bounds along z.</summary>
        public static CombinedLength Of(float minZ, float maxZ)
        {
            CombinedLength c = default;
            c.Front = maxZ;
            c.Rear = minZ;
            c.Origin = 0f;
            return c;
        }

        /// <summary>
        /// Adds the next trailer. tractorAttachZ = CarTractorData.m_AttachPosition.z of the piece that pulls it (the last
        /// added piece), trailerAttachZ = this trailer's CarTrailerData.m_AttachPosition.z, minZ/maxZ = its prefab bounds.
        /// </summary>
        public void AddTrailer(float tractorAttachZ, float trailerAttachZ, float minZ, float maxZ)
        {
            Origin = Origin + tractorAttachZ - trailerAttachZ;
            float front = Origin + maxZ;
            float rear = Origin + minZ;
            if (front > Front) Front = front;
            if (rear < Rear) Rear = rear;
        }

        /// <summary>Front-most front to rear-most rear (metres).</summary>
        public float Length => Front - Rear;
    }
}
