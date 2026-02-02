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
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GhostInstance>();

            // 1. 맵 생성 (넉넉한 용량, 필요시 자동 증가 안됨에 주의 -> 충분히 크게 잡기)
            var map = new NativeParallelHashMap<int, Entity>(32000, Allocator.Persistent);

            // 2. 싱글톤으로 등록하여 다른 시스템이 접근 가능하게 함
            state.EntityManager.CreateSingleton(new GhostIdMap { Map = map });
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            // 종료 시 메모리 해제
            if (SystemAPI.TryGetSingleton<GhostIdMap>(out var ghostIdMap))
            {
                if (ghostIdMap.Map.IsCreated) ghostIdMap.Map.Dispose();
            }
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // 싱글톤 데이터 가져오기 (RW 권한)
            var ghostIdMap = SystemAPI.GetSingletonRW<GhostIdMap>();

            // 1. 맵 초기화 (메인 스레드에서 수행해야 함)
            // Clear는 빠르므로 병렬화 불필요
            ghostIdMap.ValueRW.Map.Clear();

            // 2. 병렬 쓰기 객체 생성
            // NativeParallelHashMap을 병렬 잡에서 쓰려면 ParallelWriter가 필요함
            var mapWriter = ghostIdMap.ValueRW.Map.AsParallelWriter();

            // 3. Job 예약 (ScheduleParallel)
            var job = new PopulateGhostMapJob
            {
                MapWriter = mapWriter
            };

            // 싱글 스레드 처리 대신, 가용 가능한 모든 워커 스레드 사용
            state.Dependency = job.ScheduleParallel(state.Dependency);

            // Job 완료 대기 (다른 SystemGroup에서 안전하게 접근 가능)
            // GhostInputSystemGroup 등 다른 그룹의 시스템에서 GhostIdMap을 사용하므로
            // Job이 완료되어야 안전하게 접근 가능
            state.Dependency.Complete();
        }
    }

    // 병렬 처리를 위한 잡 정의
    [BurstCompile]
    public partial struct PopulateGhostMapJob : IJobEntity
    {
        public NativeParallelHashMap<int, Entity>.ParallelWriter MapWriter;

        // in 필터링: 읽기 전용으로 접근하여 성능 최적화
        private void Execute(Entity entity, in GhostInstance ghost)
        {
            // TryAdd를 사용하여 병렬 쓰기 충돌 방지 (GhostId는 고유하므로 충돌 확률 없음)
            MapWriter.TryAdd(ghost.ghostId, entity);
        }
    }
}