using System.Collections.Generic;
using Game.Net;
using JunctionRules.Core;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace JunctionRules.Systems
{
    /// <summary>
    /// JR-T-013b (G4): picks the junction to watch (main thread, read-only). 0.4.11 (G23, owner: the 8 m line pick missed an
    /// off-centre camera): the road node with 3 or more road arms nearest to the camera PIVOT (where the centre line meets
    /// each node's height), within 25 m (Core JunctionWatch.PickNearPivot). Boxed or not. Called by CarDrivingSwitchSystem on the settings button; never names copy types.
    /// </summary>
    internal static class JunctionWatchPick
    {
        public const double Radius = JunctionWatch.PivotRadius;

        /// <summary>The query the switch system builds in OnCreate: road nodes (not deleted / temp).</summary>
        public static EntityQueryDesc NodeQueryDesc() => new EntityQueryDesc
        {
            All = new[] { ComponentType.ReadOnly<Game.Net.Node>(), ComponentType.ReadOnly<ConnectedEdge>(), ComponentType.ReadOnly<Game.Net.Road>() },
            None = new[] { ComponentType.ReadOnly<Game.Common.Deleted>(), ComponentType.ReadOnly<Game.Tools.Temp>() },
        };

        /// <summary>The watched node key (0 = none found) and a text for the log.</summary>
        public static long PickAtCamera(EntityManager em, EntityQuery nodes, out string text)
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                foreach (Camera c in Camera.allCameras) if (c.cameraType == CameraType.Game) { cam = c; break; }
            }
            if (cam == null) { text = "no game camera found"; return 0; }
            Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            NativeArray<Entity> ents = nodes.ToEntityArray(Allocator.Temp);
            NativeArray<Game.Net.Node> data = nodes.ToComponentDataArray<Game.Net.Node>(Allocator.Temp);
            try
            {
                var pos = new List<V3>();
                var which = new List<int>();
                for (int i = 0; i < ents.Length; i++)
                {
                    if (RoadArms(em, ents[i]) < 3) continue;
                    var p = data[i].m_Position;
                    pos.Add(new V3(p.x, p.y, p.z));
                    which.Add(i);
                }
                int k = JunctionWatch.PickNearPivot(new V3(ray.origin.x, ray.origin.y, ray.origin.z), new V3(ray.direction.x, ray.direction.y, ray.direction.z), pos, Radius, out double dist);
                if (k < 0) { text = "no junction node (3 or more road arms) within 25 m of the camera's aim point"; return 0; }
                Entity e = ents[which[k]];
                text = "node " + JunctionWatch.EntityText(JunctionWatch.Key(e.Index, e.Version)) + " at " + pos[k]
                       + " (" + dist.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " m from the camera's aim point)"
                       + (em.HasComponent<JunctionRules.Boxes.YellowBoxJunction>(e) ? " (boxed)" : " (no box)");
                return JunctionWatch.Key(e.Index, e.Version);
            }
            finally
            {
                ents.Dispose();
                data.Dispose();
            }
        }

        private static int RoadArms(EntityManager em, Entity node)
        {
            if (!em.HasBuffer<ConnectedEdge>(node)) return 0;
            DynamicBuffer<ConnectedEdge> edges = em.GetBuffer<ConnectedEdge>(node, true);
            int n = 0;
            for (int i = 0; i < edges.Length; i++)
            {
                Entity e = edges[i].m_Edge;
                if (em.Exists(e) && em.HasComponent<Game.Net.Road>(e)) n++;
            }
            return n;
        }
    }
}
