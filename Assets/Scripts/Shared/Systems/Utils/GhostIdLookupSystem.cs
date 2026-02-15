using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

namespace Shared
{
    /// <summary>
    /// GhostId(int) -> Entity 변환 맵을 병렬로 생성하는 시스템
    /// </summary>
    [UpdateInGroup(typeof(PredictedSimulationSystemGroup), OrderFirst = true)]
    [BurstCompile]
    public partial struct GhostIdLookupSystem : ISystem
    {
        private const int InitialCapacity = 256;

        private EntityQuery _ghostQuery;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GhostInstance>();

            _ghostQuery = state.GetEntityQuery(ComponentType.ReadOnly<GhostInstance>());

            var map = new NativeParallelHashMap<int, Entity>(InitialCapacity, Allocator.Persistent);
            state.EntityManager.CreateSingleton(new GhostIdMap { Map = map });
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            if (SystemAPI.TryGetSingleton<GhostIdMap>(out var ghostIdMap))
            {
                if (ghostIdMap.Map.IsCreated) ghostIdMap.Map.Dispose();
            }
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var ghostIdMap = SystemAPI.GetSingletonRW<GhostIdMap>();

            var ghostCount = _ghostQuery.CalculateEntityCount();
            var map = ghostIdMap.ValueRW.Map;
            if (ghostCount > map.Capacity)
            {
                map.Capacity = ghostCount * 2;
            }

            map.Clear();

            var mapWriter = ghostIdMap.ValueRW.Map.AsParallelWriter();

            var job = new PopulateGhostMapJob
            {
                MapWriter = mapWriter
            };

            state.Dependency = job.ScheduleParallel(state.Dependency);

            // 다른 SystemGroup(GhostInputSystemGroup 등)에서 GhostIdMap을 사용하므로 완료 대기 필수
            state.Dependency.Complete();
        }
    }

    [BurstCompile]
    public partial struct PopulateGhostMapJob : IJobEntity
    {
        public NativeParallelHashMap<int, Entity>.ParallelWriter MapWriter;

        private void Execute(Entity entity, in GhostInstance ghost)
        {
            MapWriter.TryAdd(ghost.ghostId, entity);
        }
    }
}
