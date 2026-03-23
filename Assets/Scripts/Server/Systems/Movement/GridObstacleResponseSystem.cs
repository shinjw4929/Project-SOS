using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using Shared;

namespace Server
{
    struct BuildingPushInfo
    {
        public float3 Position;
        public float HalfW;
        public float HalfL;
    }

    /// <summary>
    /// 건물 건설 시 IsPathBlocked 마킹 + 유닛 밀어내기 + Flow Field 캐시 무효화
    /// NeedsNavMeshObstacle 태그로 트리거 (기존 태그 재사용)
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateBefore(typeof(FlowFieldSystem))]
    public partial struct GridObstacleResponseSystem : ISystem
    {
        const float DefaultPathInvalidationRadius = 8f;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GridSettings>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var gridSettings = SystemAPI.GetSingleton<GridSettings>();
            var gridEntity = SystemAPI.GetSingletonEntity<GridSettings>();
            var gridBuffer = SystemAPI.GetBuffer<GridCell>(gridEntity);
            int gridSizeX = gridSettings.GridSize.x;
            float pathInvalidationRadius = SystemAPI.TryGetSingleton<GameSettings>(out var gs)
                ? gs.PathInvalidationRadius : DefaultPathInvalidationRadius;

            var buildings = new NativeList<BuildingPushInfo>(4, Allocator.Temp);
            bool anyProcessed = false;

            var ecb = new EntityCommandBuffer(Allocator.Temp);

            // === 1패스: 건물 수집 + IsPathBlocked 마킹 + GridObstacleCleanup 부착 ===
            foreach (var (transform, footprint, gridPos, entity) in
                SystemAPI.Query<RefRO<LocalTransform>, RefRO<StructureFootprint>, RefRO<GridPosition>>()
                    .WithAny<StructureTag, ResourceNodeTag>()
                    .WithAll<NeedsNavMeshObstacle>()
                    .WithEntityAccess())
            {
                anyProcessed = true;

                var fp = footprint.ValueRO;
                int pathWidth = math.max(1, fp.Width - 2);
                int pathLength = math.max(1, fp.Length - 2);

                // IsPathBlocked 마킹 (경로탐색 풋프린트 중앙)
                GridUtility.MarkPathBlocked(gridBuffer,
                    gridPos.ValueRO.Position.x, gridPos.ValueRO.Position.y,
                    fp.Width, fp.Length,
                    pathWidth, pathLength,
                    gridSizeX);

                // GridObstacleCleanup 부착 (건물 파괴 감지용)
                ecb.AddComponent(entity, new GridObstacleCleanup
                {
                    GridPosition = gridPos.ValueRO.Position,
                    Width = fp.Width,
                    Length = fp.Length
                });

                // NeedsNavMeshObstacle 비활성화
                ecb.SetComponentEnabled<NeedsNavMeshObstacle>(entity, false);

                // 밀어내기 정보 수집 (항상 직사각형, Width×CellSize 파생)
                float cellSize = gridSettings.CellSize;
                float halfW = fp.Width * cellSize * 0.5f;
                float halfL = fp.Length * cellSize * 0.5f;

                buildings.Add(new BuildingPushInfo
                {
                    Position = transform.ValueRO.Position,
                    HalfW = halfW,
                    HalfL = halfL
                });
            }

            if (!anyProcessed)
                return;

            // Flow Field 캐시 무효화
            var cache = SystemAPI.GetSingleton<FlowFieldCacheData>();
            cache.IsGridStale = true;
            SystemAPI.SetSingleton(cache);

            ecb.Playback(state.EntityManager);
            ecb.Dispose();

            // === 2패스: 주변 유닛 밀어내기 + IsPathDirty ===
            float pathRadiusSq = pathInvalidationRadius * pathInvalidationRadius;

            foreach (var (goal, entityTransform, obstacle, velocity, waypointsEnabled) in
                SystemAPI.Query<RefRW<MovementGoal>, RefRW<LocalTransform>, RefRO<ObstacleRadius>,
                    RefRW<PhysicsVelocity>, EnabledRefRW<MovementWaypoints>>()
                    .WithAny<UnitTag, EnemyTag>()
                    .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
            {
                for (int b = 0; b < buildings.Length; b++)
                {
                    var bld = buildings[b];
                    float3 local = entityTransform.ValueRO.Position - bld.Position;
                    local.y = 0;
                    float entityR = obstacle.ValueRO.Radius;

                    bool isInside = math.abs(local.x) < bld.HalfW + entityR &&
                                    math.abs(local.z) < bld.HalfL + entityR;

                    if (isInside)
                    {
                        float overlapX = (bld.HalfW + entityR) - math.abs(local.x);
                        float overlapZ = (bld.HalfL + entityR) - math.abs(local.z);

                        if (overlapX < overlapZ)
                        {
                            float sign = local.x >= 0 ? 1f : -1f;
                            entityTransform.ValueRW.Position.x += sign * (overlapX + 0.1f);
                        }
                        else
                        {
                            float sign = local.z >= 0 ? 1f : -1f;
                            entityTransform.ValueRW.Position.z += sign * (overlapZ + 0.1f);
                        }

                        waypointsEnabled.ValueRW = false;
                        velocity.ValueRW.Linear = float3.zero;
                        goal.ValueRW.IsPathDirty = true;
                    }
                    else if (math.lengthsq(local) < pathRadiusSq && waypointsEnabled.ValueRO)
                    {
                        goal.ValueRW.IsPathDirty = true;
                    }
                }
            }
        }
    }
}
