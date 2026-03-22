using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Shared;

namespace Server
{
    /// <summary>
    /// 건물 파괴 시 IsPathBlocked 해제 + Flow Field 캐시 무효화 + Partial Path 무효화 + Dormant 깨우기
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateAfter(typeof(ServerDeathSystem))]
    [UpdateBefore(typeof(FlowFieldSystem))]
    public partial struct GridObstacleCleanupSystem : ISystem
    {
        const float PartialPathInvalidationRadius = 12f;

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

            var ecb = new EntityCommandBuffer(Allocator.Temp);
            bool anyProcessed = false;
            var destroyedPositions = new NativeList<float3>(4, Allocator.Temp);

            // 파괴된 건물 감지: GridObstacleCleanup 있지만 StructureTag/ResourceNodeTag 없음
            foreach (var (cleanup, entity) in
                SystemAPI.Query<RefRO<GridObstacleCleanup>>()
                    .WithNone<StructureTag, ResourceNodeTag>()
                    .WithEntityAccess())
            {
                anyProcessed = true;

                var data = cleanup.ValueRO;

                // IsPathBlocked 해제 (경로탐색 풋프린트)
                GridUtility.UnmarkPathBlocked(gridBuffer,
                    data.GridPosition.x, data.GridPosition.y,
                    data.Width, data.Length,
                    data.PathWidth, data.PathLength,
                    gridSizeX);

                // 월드 좌표 복원 (밀어내기/Dormant 깨우기 반경 계산용)
                float3 worldPos = GridUtility.GridToWorld(
                    data.GridPosition.x, data.GridPosition.y,
                    data.Width, data.Length, gridSettings);
                destroyedPositions.Add(worldPos);

                // GridObstacleCleanup 제거
                ecb.RemoveComponent<GridObstacleCleanup>(entity);
            }

            if (!anyProcessed)
                return;

            // Flow Field 캐시 무효화
            if (SystemAPI.TryGetSingleton<FlowFieldCacheData>(out var cache))
            {
                cache.IsGridStale = true;
                SystemAPI.SetSingleton(cache);
            }

            ecb.Playback(state.EntityManager);
            ecb.Dispose();

            // 주변 Partial Path 무효화 + Dormant 적 깨우기
            float radiusSq = PartialPathInvalidationRadius * PartialPathInvalidationRadius;

            // EnemyTag: Dormant 깨우기 + Partial Path 무효화
            foreach (var (goal, transform, enemyState) in
                SystemAPI.Query<RefRW<MovementGoal>, RefRO<LocalTransform>, RefRW<EnemyState>>()
                    .WithAll<EnemyTag>())
            {
                float3 entityPos = transform.ValueRO.Position;

                for (int i = 0; i < destroyedPositions.Length; i++)
                {
                    if (math.distancesq(entityPos, destroyedPositions[i]) >= radiusSq)
                        continue;

                    if (enemyState.ValueRO.CurrentState == EnemyContext.Dormant)
                    {
                        enemyState.ValueRW.CurrentState = EnemyContext.Idle;
                    }
                    else if (goal.ValueRO.IsPathPartial)
                    {
                        goal.ValueRW.IsPathDirty = true;
                    }
                    break; // 하나라도 범위 내면 처리 완료
                }
            }

            // UnitTag: Partial Path 무효화만
            foreach (var (goal, transform) in
                SystemAPI.Query<RefRW<MovementGoal>, RefRO<LocalTransform>>()
                    .WithAll<UnitTag>())
            {
                if (!goal.ValueRO.IsPathPartial) continue;

                for (int i = 0; i < destroyedPositions.Length; i++)
                {
                    if (math.distancesq(transform.ValueRO.Position, destroyedPositions[i]) < radiusSq)
                    {
                        goal.ValueRW.IsPathDirty = true;
                        break;
                    }
                }
            }
        }
    }
}
