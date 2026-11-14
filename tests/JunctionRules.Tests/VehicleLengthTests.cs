using System;
using System.IO;
using JunctionRules.Core;

namespace JunctionRules.Tests
{
    /// <summary>
    /// 0.2.2 (JR-T-002d): mid-junction waiting spots need room for the whole vehicle, trailers included
    /// (Core/VehicleLength.cs; job side Stage2/JRVehicleLength.cs, navigation block S2-25).
    /// Attach points as the game builds them (VehicleInitializeSystem): tractor z = bounds.min.z + CarTractor.m_AttachOffset.z,
    /// trailer z = bounds.max.z - CarTrailerPrefab.m_AttachOffset.z; the trailer's attach point sits on the tractor's.
    /// </summary>
    internal static partial class Program
    {
        private static float TractorAttach(float minZ, float offsetZ) => minZ + offsetZ;
        private static float TrailerAttach(float maxZ, float offsetZ) => maxZ - offsetZ;

        private static void VehicleLengthTests()
        {
            // Car without trailers: exactly the 0.2.1 length (bounds front to rear).
            var car = CombinedLength.Of(-2.2f, 2.3f);
            Check(Near(car.Length, 4.5f), "no trailer: length = controller bounds (0.2.1 value)");

            // Hitch points at the very ends (offset 0): the pieces abut, length = plain sum.
            var abut = CombinedLength.Of(-3f, 3f);
            abut.AddTrailer(TractorAttach(-3f, 0f), TrailerAttach(4f, 0f), -4f, 4f);
            Check(Near(abut.Length, 6f + 8f), "hitch at both ends: tractor + trailer lengths add up");

            // Semi-trailer: tractor 6 m (z -3..3), fifth wheel 1.5 m in from its rear; trailer 13 m (z -6.5..6.5), king pin
            // 1 m behind its front. Trailer origin = -1.5 - 5.5 = -7, rear = -13.5, its front (-0.5) stays behind the
            // tractor's front: overall 3 - (-13.5) = 16.5 m (the overhang over the tractor counted once).
            var semi = CombinedLength.Of(-3f, 3f);
            semi.AddTrailer(TractorAttach(-3f, 1.5f), TrailerAttach(6.5f, 1f), -6.5f, 6.5f);
            Check(Near(semi.Length, 16.5f), "semi-trailer: straight extent tractor front to trailer rear (16.5 m)");
            Check(semi.Length < 6f + 13f, "semi-trailer overhang is not counted twice");

            // Multi-trailer: a drawbar trailer behind the semi (it carries its own CarTractorData: attach 0.5 m in from its
            // rear), 7 m (z -3.5..3.5), drawbar eye 2 m ahead of its front (offset -2 -> attach z 5.5).
            var road = semi;
            road.AddTrailer(TractorAttach(-6.5f, 0.5f), TrailerAttach(3.5f, -2f), -3.5f, 3.5f);
            // Its origin = -7 + (-6) - 5.5 = -18.5, rear = -22 -> 3 - (-22) = 25 m (the 2 m drawbar minus the 0.5 m hook
            // inset leaves 1.5 m between the semi's rear and the drawbar trailer's front: 16.5 + 1.5 + 7).
            Check(Near(road.Length, 25f), "two trailers: chained attach points (25 m)");
            var twoAbut = CombinedLength.Of(-2f, 2f);
            twoAbut.AddTrailer(-2f, 3f, -3f, 3f);
            twoAbut.AddTrailer(-3f, 1.5f, -1.5f, 1.5f);
            Check(Near(twoAbut.Length, 4f + 6f + 3f), "two trailers hitched at their ends: sum of all three lengths");

            // Mid spots: the divided road of StretchSpotTests (8 m central reservation on a 40 m node lane).
            var divided = new[] { C(0.05f, 0.15f), C(0.15f, 0.25f), C(0.45f, 0.55f), C(0.55f, 0.65f) };
            var tractorOnly = Car(40f, 0f, 0f, CombinedLength.Of(-3f, 3f).Length, divided);
            var withTrailer = Car(40f, 0f, 0f, semi.Length, divided);
            Check(tractorOnly.Spots.Count == 2, "tractor alone (6 m) fits the 8 m reservation: mid spot");
            Check(withTrailer.Spots.Count == 1, "tractor + trailer (16.5 m) does not fit the 8 m reservation: no mid spot, one decision at W");
            var carSpots = Car(40f, 0f, 0f, car.Length, divided);
            var carOld = Car(40f, 0f, 0f, 4.5f, divided);
            Check(carSpots.Spots.Count == carOld.Spots.Count && Near(carSpots.Spots[1], carOld.Spots[1]), "car without trailer: same spots as 0.2.1");
            // A reservation long enough for the whole articulated truck gives it the mid spot again.
            var wide = new[] { C(0.05f, 0.10f), C(0.10f, 0.15f), C(0.65f, 0.75f) };   // 20 m gap on 40 m
            Check(Car(40f, 0f, 0f, semi.Length, wide).Spots.Count == 2, "20 m reservation fits the 16.5 m truck + margin: mid spot");
            Check(Car(40f, 0f, 0f, road.Length, wide).Spots.Count == 1, "25 m road train does not fit the 20 m reservation");

            // Repo: the job side and the navigation block wire the length in, only when stage 2 is on.
            string root = RepoRoot();
            if (root == null) { Check(false, "repo root found"); return; }
            string nav = File.ReadAllText(Path.Combine(root, "JunctionRules", "Vanilla", "JRCarNavigationSystem.cs"));
            Check(nav.Contains("if (m_JRMode != 0 && bufferAccessor3.Length != 0)") && nav.Contains("JRVehicleLength.WithTrailers(objectGeometryData2, bufferAccessor3[j]"),
                "S2-25 computes the whole length from the layout buffer only when stage 2 is on");
            int s25 = nav.IndexOf("BEGIN S2-25", StringComparison.Ordinal);
            int target = nav.IndexOf("UpdateNavigationTarget(ref random, priority, entity2", StringComparison.Ordinal);
            Check(s25 > 0 && target > s25, "S2-25 runs before UpdateNavigationTarget (which builds the iterator)");
            Check(nav.Contains("JRWholeJunction.Create(m_JRMode, m_NodeLaneData, m_JRVehicleLength)"), "S2-21 hands the length to the iterator");
            Check(nav.Contains("GetComponentLookup<CarTractorData>(isReadOnly: true)") && nav.Contains("GetComponentLookup<CarTrailerData>(isReadOnly: true)"), "S2-23 sets the read-only prefab lookups");
            string jr = File.ReadAllText(Path.Combine(root, "JunctionRules", "Stage2", "JRWholeJunction.cs"));
            Check(jr.Contains("m_VehicleLength > 0f ? m_VehicleLength : geometry.m_Bounds.max.z - geometry.m_Bounds.min.z"), "BeginLane uses the whole length, else the 0.2.1 bounds length");
            string vl = File.ReadAllText(Path.Combine(root, "JunctionRules", "Stage2", "JRVehicleLength.cs"));
            string core = File.ReadAllText(Path.Combine(root, "JunctionRules", "Core", "VehicleLength.cs"));
            foreach (var (name, text) in new[] { ("JRVehicleLength", vl), ("CombinedLength", core) })
            {
                string code = System.Text.RegularExpressions.Regex.Replace(text, @"//.*", "");
                Check(!System.Text.RegularExpressions.Regex.IsMatch(code, @"\bnew\s+\w"), $"{name}: no 'new' (no managed allocation)");
            }
            string decomp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Code", "decomp", "yb-2026-11-13", "full");
            string vis = Path.Combine(decomp, "Game.Prefabs", "VehicleInitializeSystem.cs");
            string init = Path.Combine(decomp, "Game.Vehicles", "InitializeSystem.cs");
            if (File.Exists(vis) && File.Exists(init))
            {
                string v = File.ReadAllText(vis), i = File.ReadAllText(init);
                Check(v.Contains("value4.m_AttachPosition.z = objectGeometryData2.m_Bounds.min.z + component.m_AttachOffset.z;")
                      && v.Contains("value5.m_AttachPosition.z = objectGeometryData3.m_Bounds.max.z - prefab3.m_AttachOffset.z;"),
                    "decompile: attach points from prefab bounds + stock offsets (as mirrored here)");
                Check(i.Contains("transform2.m_Position += math.rotate(transform.m_Rotation, carTractorData.m_AttachPosition);")
                      && i.Contains("transform2.m_Position -= math.rotate(transform2.m_Rotation, carTrailerData.m_AttachPosition);"),
                    "decompile: trailer attach point placed on the tractor's attach point (no extra hitch gap)");
            }
            else
            {
                Console.WriteLine("note: decompile not found, trailer placement not cross-checked");
            }
        }
    }
}
