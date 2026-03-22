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
        public bool IsCircular;
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
        const float PathInvalidationRadius = 8f;

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

                // IsPathBlocked 마킹 (경로탐색 풋프린트 중앙)
                GridUtility.MarkPathBlocked(gridBuffer,
                    gridPos.ValueRO.Position.x, gridPos.ValueRO.Position.y,
                    footprint.ValueRO.Width, footprint.ValueRO.Length,
                    footprint.ValueRO.PathWidth, footprint.ValueRO.PathLength,
                    gridSizeX);

                // GridObstacleCleanup 부착 (건물 파괴 감지용)
                ecb.AddComponent(entity, new GridObstacleCleanup
                {
                    GridPosition = gridPos.ValueRO.Position,
                    Width = footprint.ValueRO.Width,
                    Length = footprint.ValueRO.Length,
                    PathWidth = footprint.ValueRO.PathWidth,
                    PathLength = footprint.ValueRO.PathLength,
                });

                // NeedsNavMeshObstacle 비활성화
                ecb.SetComponentEnabled<NeedsNavMeshObstacle>(entity, false);

                // 밀어내기 정보 수집
                var fp = footprint.ValueRO;
                float halfW, halfL;
                if (fp.IsCircular)
                {
                    halfW = halfL = fp.WorldRadius;
                }
                else
                {
                    halfW = fp.WorldWidth * 0.5f;
                    halfL = fp.WorldLength * 0.5f;
                }

                buildings.Add(new BuildingPushInfo
                {
                    Position = transform.ValueRO.Position,
                    HalfW = halfW,
                    HalfL = halfL,
                    IsCircular = fp.IsCircular,
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
            float pathRadiusSq = PathInvalidationRadius * PathInvalidationRadius;

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

                    bool isInside;
                    if (bld.IsCircular)
                    {
                        float dist = math.length(local);
                        isInside = dist < bld.HalfW + entityR;
                    }
                    else
                    {
                        isInside = math.abs(local.x) < bld.HalfW + entityR &&
                                   math.abs(local.z) < bld.HalfL + entityR;
                    }

                    if (isInside)
                    {
                        // 밀어내기
                        if (bld.IsCircular)
                        {
                            float dist = math.length(local);
                            float pushDist = bld.HalfW + entityR - dist;
                            float3 pushDir = dist > 0.01f ? local / dist : new float3(1, 0, 0);
                            entityTransform.ValueRW.Position += pushDir * (pushDist + 0.1f);
                        }
                        else
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
