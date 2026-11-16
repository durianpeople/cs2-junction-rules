// COPIED GAME CODE (JR-T-013d, gap G6). Source: Game.Pathfind.ParkingLaneDataSystem.UpdateLaneDataJob, Game.dll 1.6.4f1
// (decompile ~/Code/decomp/yb-2026-11-13/full/Game.Pathfind/ParkingLaneDataSystem.cs): GetBlockedRange lines 310-334 and
// CalculateFreeSpace lines 547-731, method bodies unchanged (checked by the test runner's ParkingRepoTests against the
// decompile). Deviations (DEVIATIONS.md section G6): the class, `internal` instead of `private`, and the laneObjects /
// laneOverlaps parameters are NativeArray instead of DynamicBuffer (same Length / indexer; the caller passes the sorted
// lane objects, as the job sorts them first at line 226, and the lane's overlaps without the mod's own entries).
// Used ONLY right before a save (Parking/BoxParkingSystem.PrepareForSave) to put the game's own free-space value back on
// the parking lanes the mod blocks, so the save holds what the game would have stored without the mod.
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Prefabs;
using Game.Vehicles;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace JunctionRules.Parking
{
	internal struct ParkingFreeSpaceCopy
	{
		public ComponentLookup<Lane> m_LaneData;

		public ComponentLookup<Game.Net.CarLane> m_CarLaneData;

		public ComponentLookup<ParkedCar> m_ParkedCarData;

		public ComponentLookup<Unspawned> m_UnspawnedData;

		public ComponentLookup<PrefabRef> m_PrefabRefData;

		public ComponentLookup<ObjectGeometryData> m_ObjectGeometryData;

		public BufferLookup<Game.Net.SubLane> m_SubLanes;

		internal Bounds1 GetBlockedRange(Owner owner, Lane laneData)
		{
			Bounds1 result = new Bounds1(2f, -1f);
			if (m_SubLanes.HasBuffer(owner.m_Owner))
			{
				DynamicBuffer<Game.Net.SubLane> dynamicBuffer = m_SubLanes[owner.m_Owner];
				for (int i = 0; i < dynamicBuffer.Length; i++)
				{
					Entity subLane = dynamicBuffer[i].m_SubLane;
					Lane lane = m_LaneData[subLane];
					if (laneData.m_StartNode.EqualsIgnoreCurvePos(lane.m_MiddleNode) && m_CarLaneData.HasComponent(subLane))
					{
						Game.Net.CarLane carLane = m_CarLaneData[subLane];
						if (carLane.m_BlockageEnd >= carLane.m_BlockageStart)
						{
							Bounds1 blockageBounds = carLane.blockageBounds;
							blockageBounds.min = math.select(blockageBounds.min - 0.01f, 0f, blockageBounds.min <= 0.51f);
							blockageBounds.max = math.select(blockageBounds.max + 0.01f, 1f, blockageBounds.max >= 0.49f);
							result |= blockageBounds;
						}
					}
				}
			}
			return result;
		}

		internal float CalculateFreeSpace(Curve curve, Game.Net.ParkingLane parkingLane, ParkingLaneData parkingLaneData, NativeArray<LaneObject> laneObjects, NativeArray<LaneOverlap> laneOverlaps, Bounds1 blockedRange)
		{
			if ((parkingLane.m_Flags & ParkingLaneFlags.VirtualLane) != 0)
			{
				return 0f;
			}
			if (parkingLaneData.m_SlotInterval != 0f)
			{
				int parkingSlotCount = NetUtils.GetParkingSlotCount(curve, parkingLane, parkingLaneData);
				float parkingSlotInterval = NetUtils.GetParkingSlotInterval(curve, parkingLane, parkingLaneData, parkingSlotCount);
				float3 x = curve.m_Bezier.a;
				float2 @float = 0f;
				float num = 0f;
				float num2 = math.max((parkingLane.m_Flags & (ParkingLaneFlags.StartingLane | ParkingLaneFlags.EndingLane)) switch
				{
					ParkingLaneFlags.StartingLane => curve.m_Length - (float)parkingSlotCount * parkingSlotInterval, 
					ParkingLaneFlags.EndingLane => 0f, 
					_ => (curve.m_Length - (float)parkingSlotCount * parkingSlotInterval) * 0.5f, 
				}, 0f);
				int i = -1;
				float num3 = 2f;
				int num4 = 0;
				while (num4 < laneObjects.Length)
				{
					LaneObject laneObject = laneObjects[num4++];
					if (m_ParkedCarData.HasComponent(laneObject.m_LaneObject) && !m_UnspawnedData.HasComponent(laneObject.m_LaneObject))
					{
						num3 = laneObject.m_CurvePosition.x;
						break;
					}
				}
				float2 float2 = 2f;
				int num5 = 0;
				if (num5 < laneOverlaps.Length)
				{
					LaneOverlap laneOverlap = laneOverlaps[num5++];
					float2 = new float2((int)laneOverlap.m_ThisStart, (int)laneOverlap.m_ThisEnd) * 0.003921569f;
				}
				for (int j = 1; j <= 16; j++)
				{
					float num6 = (float)j * 0.0625f;
					float3 float3 = MathUtils.Position(curve.m_Bezier, num6);
					for (num += math.distance(x, float3); num >= num2 || (j == 16 && i < parkingSlotCount); i++)
					{
						@float.y = math.select(num6, math.lerp(@float.x, num6, num2 / num), num2 < num);
						bool flag = false;
						if (num3 <= @float.y)
						{
							num3 = 2f;
							flag = true;
							while (num4 < laneObjects.Length)
							{
								LaneObject laneObject2 = laneObjects[num4++];
								if (m_ParkedCarData.HasComponent(laneObject2.m_LaneObject) && !m_UnspawnedData.HasComponent(laneObject2.m_LaneObject) && laneObject2.m_CurvePosition.x > @float.y)
								{
									num3 = laneObject2.m_CurvePosition.x;
									break;
								}
							}
						}
						if (float2.x < @float.y)
						{
							flag = true;
							if (float2.y <= @float.y)
							{
								float2 = 2f;
								while (num5 < laneOverlaps.Length)
								{
									LaneOverlap laneOverlap2 = laneOverlaps[num5++];
									float2 float4 = new float2((int)laneOverlap2.m_ThisStart, (int)laneOverlap2.m_ThisEnd) * 0.003921569f;
									if (float4.y > @float.y)
									{
										float2 = float4;
										break;
									}
								}
							}
						}
						if (!flag && i >= 0 && i < parkingSlotCount && (@float.x > blockedRange.max || @float.y < blockedRange.min))
						{
							return parkingLaneData.m_MaxCarLength;
						}
						num -= num2;
						@float.x = @float.y;
						num2 = parkingSlotInterval;
					}
					x = float3;
				}
				return 0f;
			}
			float x2 = 0f;
			float2 x3 = math.select(0f, 0.5f, (parkingLane.m_Flags & ParkingLaneFlags.StartingLane) == 0);
			float3 x4 = curve.m_Bezier.a;
			float num7 = 2f;
			float2 float5 = 0f;
			int num8 = 0;
			while (num8 < laneObjects.Length)
			{
				LaneObject laneObject3 = laneObjects[num8++];
				if (m_ParkedCarData.HasComponent(laneObject3.m_LaneObject) && !m_UnspawnedData.HasComponent(laneObject3.m_LaneObject))
				{
					num7 = laneObject3.m_CurvePosition.x;
					float5 = VehicleUtils.GetParkingOffsets(laneObject3.m_LaneObject, ref m_PrefabRefData, ref m_ObjectGeometryData) + 1f;
					break;
				}
			}
			float2 float6 = 2f;
			int num9 = 0;
			if (num9 < laneOverlaps.Length)
			{
				LaneOverlap laneOverlap3 = laneOverlaps[num9++];
				float6 = new float2((int)laneOverlap3.m_ThisStart, (int)laneOverlap3.m_ThisEnd) * 0.003921569f;
			}
			float3 y = default(float3);
			float3 float7 = default(float3);
			if (blockedRange.max >= blockedRange.min)
			{
				y = MathUtils.Position(curve.m_Bezier, MathUtils.Center(blockedRange));
				float7.x = math.distance(MathUtils.Position(curve.m_Bezier, blockedRange.min), y);
				float7.y = math.distance(MathUtils.Position(curve.m_Bezier, blockedRange.max), y);
			}
			float num10;
			while (num7 != 2f || float6.x != 2f)
			{
				float2 float8;
				float x5;
				if (num7 <= float6.x)
				{
					float8 = num7;
					x3.y = float5.x;
					x5 = float5.y;
					num7 = 2f;
					while (num8 < laneObjects.Length)
					{
						LaneObject laneObject4 = laneObjects[num8++];
						if (m_ParkedCarData.HasComponent(laneObject4.m_LaneObject) && !m_UnspawnedData.HasComponent(laneObject4.m_LaneObject))
						{
							num7 = laneObject4.m_CurvePosition.x;
							float5 = VehicleUtils.GetParkingOffsets(laneObject4.m_LaneObject, ref m_PrefabRefData, ref m_ObjectGeometryData) + 1f;
							break;
						}
					}
				}
				else
				{
					float8 = float6;
					x3.y = 0.5f;
					x5 = 0.5f;
					float6 = 2f;
					while (num9 < laneOverlaps.Length)
					{
						LaneOverlap laneOverlap4 = laneOverlaps[num9++];
						float2 float9 = new float2((int)laneOverlap4.m_ThisStart, (int)laneOverlap4.m_ThisEnd) * 0.003921569f;
						if (float9.x <= float8.y)
						{
							float8.y = math.max(float8.y, float9.y);
							continue;
						}
						float6 = float9;
						break;
					}
				}
				float3 float10 = MathUtils.Position(curve.m_Bezier, float8.x);
				num10 = math.distance(x4, float10) - math.csum(x3);
				if (blockedRange.max >= blockedRange.min)
				{
					float x6 = math.distance(x4, y) - x3.x - float7.x;
					float y2 = math.distance(float10, y) - x3.y - float7.y;
					num10 = math.min(num10, math.max(x6, y2));
				}
				x2 = math.max(x2, num10);
				x3.x = x5;
				x4 = MathUtils.Position(curve.m_Bezier, float8.y);
			}
			x3.y = math.select(0f, 0.5f, (parkingLane.m_Flags & ParkingLaneFlags.EndingLane) == 0);
			num10 = math.distance(x4, curve.m_Bezier.d) - math.csum(x3);
			if (blockedRange.max >= blockedRange.min)
			{
				float x7 = math.distance(x4, y) - x3.x - float7.x;
				float y3 = math.distance(curve.m_Bezier.d, y) - x3.y - float7.y;
				num10 = math.min(num10, math.max(x7, y3));
			}
			x2 = math.max(x2, num10);
			return math.select(x2, math.min(x2, parkingLaneData.m_MaxCarLength), parkingLaneData.m_MaxCarLength != 0f);
		}
	}
}
