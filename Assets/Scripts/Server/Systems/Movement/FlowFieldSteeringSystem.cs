using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Shared;

namespace Server
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateAfter(typeof(FlowFieldSystem))]
    [UpdateBefore(typeof(PredictedMovementSystem))]
    public partial struct FlowFieldSteeringSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GridSettings>();
            state.RequireForUpdate<FlowFieldCacheData>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var gridSettings = SystemAPI.GetSingleton<GridSettings>();
            var cache = SystemAPI.GetSingleton<FlowFieldCacheData>();
            int gridSizeX = gridSettings.GridSize.x;
            int gridSizeY = gridSettings.GridSize.y;
            int gridCellCount = cache.GridCellCount;

            foreach (var (goal, waypoints, flowFieldRef, pathSize, transform, entity) in
                SystemAPI.Query<
                    RefRW<MovementGoal>,
                    RefRW<MovementWaypoints>,
                    RefRO<FlowFieldRef>,
                    RefRO<GridPathfindingSize>,
                    RefRO<LocalTransform>>()
                    .WithNone<FlyingTag>()
                    .WithAny<UnitTag, EnemyTag>()
                    .WithEntityAccess())
            {
                int key = flowFieldRef.ValueRO.Key;

                // 아직 Flow Field 미할당
                if (key == -1)
                    continue;

                byte cellPadding = pathSize.ValueRO.CellPadding;
                NativeHashMap<int, int> keyToPool;
                NativeArray<byte> fieldPool;

                if (cellPadding == 0)
                {
                    keyToPool = cache.SmallKeyToPoolIndex;
                    fieldPool = cache.SmallFieldPool;
                }
                else
                {
                    keyToPool = cache.LargeKeyToPoolIndex;
                    fieldPool = cache.LargeFieldPool;
                }

                // 캐시 미스 (무효화 후 발생) → lazy re-pathing
                if (!keyToPool.TryGetValue(key, out int poolIndex))
                {
                    goal.ValueRW.IsPathDirty = true;
                    continue;
                }

                var field = fieldPool.GetSubArray(poolIndex * gridCellCount, gridCellCount);

                int2 currentCell = GridUtility.WorldToGrid(transform.ValueRO.Position, gridSettings);
                currentCell = math.clamp(currentCell, int2.zero, new int2(gridSizeX - 1, gridSizeY - 1));

                // 목적지 셀 판정: 좌표 비교 (방향=255 판정은 도달 불가와 혼동됨)
                int2 destCell = GridUtility.WorldToGrid(goal.ValueRO.Destination, gridSettings);
                destCell = math.clamp(destCell, int2.zero, new int2(gridSizeX - 1, gridSizeY - 1));

                if (currentCell.x == destCell.x && currentCell.y == destCell.y)
                {
                    // 목적지 셀 도착
                    waypoints.ValueRW.Current = goal.ValueRO.Destination;
                    waypoints.ValueRW.HasNext = false;
                    continue;
                }

                int currentIndex = currentCell.y * gridSizeX + currentCell.x;
                byte dir = field[currentIndex];

                if (dir == FlowFieldCore.DirNone)
                {
                    // 도달 불가 셀 — 재경로 요청
                    goal.ValueRW.IsPathDirty = true;
                    continue;
                }

                // 다음 셀 계산
                int2 nextCell = currentCell + FlowFieldCore.GetDirectionOffset(dir);
                nextCell = math.clamp(nextCell, int2.zero, new int2(gridSizeX - 1, gridSizeY - 1));

                // 다음 셀이 목적지인 경우
                if (nextCell.x == destCell.x && nextCell.y == destCell.y)
                {
                    waypoints.ValueRW.Current = GridUtility.CellCenterToWorld(nextCell, gridSettings);
                    waypoints.ValueRW.Next = goal.ValueRO.Destination;
                    waypoints.ValueRW.HasNext = true;
                    continue;
                }

                // 중간 셀 — 2단계 look-ahead
                waypoints.ValueRW.Current = GridUtility.CellCenterToWorld(nextCell, gridSettings);

                int nextIndex = nextCell.y * gridSizeX + nextCell.x;
                byte nextDir = field[nextIndex];

                if (nextDir == FlowFieldCore.DirNone)
                {
                    // look-ahead 도달 불가 — look-ahead 중단
                    waypoints.ValueRW.HasNext = false;
                    continue;
                }

                int2 lookAheadCell = nextCell + FlowFieldCore.GetDirectionOffset(nextDir);
                lookAheadCell = math.clamp(lookAheadCell, int2.zero, new int2(gridSizeX - 1, gridSizeY - 1));

                // look-ahead 셀이 목적지인 경우
                if (lookAheadCell.x == destCell.x && lookAheadCell.y == destCell.y)
                {
                    waypoints.ValueRW.Next = goal.ValueRO.Destination;
                    waypoints.ValueRW.HasNext = true;
                }
                else
                {
                    waypoints.ValueRW.Next = GridUtility.CellCenterToWorld(lookAheadCell, gridSettings);
                    waypoints.ValueRW.HasNext = true;
                }
            }
        }
    }
}
