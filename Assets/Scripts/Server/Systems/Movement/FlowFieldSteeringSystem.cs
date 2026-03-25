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
        [ReadOnly] private ComponentLookup<BuildApproachRadius> _buildApproachLookup;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GridSettings>();
            state.RequireForUpdate<FlowFieldCacheData>();

            _buildApproachLookup = state.GetComponentLookup<BuildApproachRadius>(true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            _buildApproachLookup.Update(ref state);

            var gridSettings = SystemAPI.GetSingleton<GridSettings>();
            var cache = SystemAPI.GetSingleton<FlowFieldCacheData>();
            int gridSizeX = gridSettings.GridSize.x;
            int gridSizeY = gridSettings.GridSize.y;
            int gridCellCount = cache.GridCellCount;

            foreach (var (goal, waypoints, flowFieldRef, pathSize, obstacle, transform, entity) in
                SystemAPI.Query<
                    RefRW<MovementGoal>,
                    RefRW<MovementWaypoints>,
                    RefRO<FlowFieldRef>,
                    RefRO<GridPathfindingSize>,
                    RefRO<ObstacleRadius>,
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

                // BuildApproachRadius 기반 조기 정지:
                // AABB 표면에서 stopDistance 이내 진입 시 웨이포인트 생성 중단
                if (_buildApproachLookup.TryGetComponent(entity, out var buildApproach))
                {
                    float surfaceDistSq = ArrivalUtility.DistanceSqToAABBSurfaceXZ(
                        transform.ValueRO.Position, buildApproach.Center,
                        buildApproach.HalfW, buildApproach.HalfL);
                    if (surfaceDistSq < buildApproach.Value * buildApproach.Value)
                    {
                        waypoints.ValueRW.Current = transform.ValueRO.Position;
                        waypoints.ValueRW.HasNext = false;
                        continue;
                    }
                }

                if (currentCell.x == destCell.x && currentCell.y == destCell.y)
                {
                    // 목적지 셀 도착 — 현재 위치로 설정하여 즉시 도착 판정 트리거
                    // (정확한 Destination 좌표로 유도하면 다수 유닛이 동일 지점으로 수렴 → 진동)
                    waypoints.ValueRW.Current = transform.ValueRO.Position;
                    waypoints.ValueRW.HasNext = false;
                    continue;
                }

                // 인접 셀(Chebyshev 거리 1)에서 월드 거리 기준 충분히 가까운 경우 도착 처리
                // Separation에 의해 목적지 셀에서 밀린 유닛의 재수렴 진동 방지
                int2 cellDiff = math.abs(currentCell - destCell);
                if (cellDiff.x <= 1 && cellDiff.y <= 1)
                {
                    float3 destPos = goal.ValueRO.Destination;
                    destPos.y = transform.ValueRO.Position.y;
                    float distToDestSq = math.lengthsq(transform.ValueRO.Position - destPos);
                    float nearR = obstacle.ValueRO.Radius;
                    if (distToDestSq < nearR * nearR)
                    {
                        waypoints.ValueRW.Current = transform.ValueRO.Position;
                        waypoints.ValueRW.HasNext = false;
                        continue;
                    }
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
