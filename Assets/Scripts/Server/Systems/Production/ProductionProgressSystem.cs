using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;
using Unity.Mathematics;
using Unity.Burst;
using Unity.Collections;
using Shared;

namespace Server
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [BurstCompile]
    public partial struct ProductionProgressSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
            state.RequireForUpdate<UnitCatalog>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float deltaTime = SystemAPI.Time.DeltaTime;
            
            // 유닛 카탈로그 버퍼 가져오기 (Job에 넘기기 위해 NativeArray로 복사하거나, Job에서 읽기 전용 접근)
            var catalogEntity = SystemAPI.GetSingletonEntity<UnitCatalog>();
            var catalogBuffer = SystemAPI.GetBuffer<UnitCatalogElement>(catalogEntity);
            
            // ECB (병렬 쓰기 가능)
            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();

            // Job 스케줄링
            new ProductionUpdateJob
            {
                DeltaTime = deltaTime,
                CatalogBuffer = catalogBuffer.AsNativeArray(), // 읽기 전용 NativeArray로 변환
                Ecb = ecb
            }.ScheduleParallel();
        }
    }

    [BurstCompile]
    [WithAll(typeof(ProductionFacilityTag))] // 태그 필터링
    public partial struct ProductionUpdateJob : IJobEntity
    {
        public float DeltaTime;
        [ReadOnly] public NativeArray<UnitCatalogElement> CatalogBuffer;
        public EntityCommandBuffer.ParallelWriter Ecb;

        // 필요한 컴포넌트만 ref로 가져옴
        private void Execute(
            [EntityIndexInQuery] int sortKey, 
            Entity entity, 
            ref ProductionQueue queue, 
            in LocalTransform transform, 
            in GhostOwner owner)
        {
            if (!queue.IsActive) return;

            // 1. 진행도 업데이트
            queue.Progress += DeltaTime;

            // 2. 완료 체크
            if (queue.Progress >= queue.Duration)
            {
                int unitIndex = queue.ProducingUnitIndex;

                if (unitIndex >= 0 && unitIndex < CatalogBuffer.Length)
                {
                    Entity prefab = CatalogBuffer[unitIndex].PrefabEntity;

                    // 유닛 스폰 명령 예약
                    SpawnUnit(sortKey, prefab, transform.Position, owner.NetworkId);
                }
                
                // Queue 초기화
                queue = new ProductionQueue
                {
                    ProducingUnitIndex = -1,
                    Progress = 0,
                    Duration = 0,
                    IsActive = false
                };
            }
        }

        private void SpawnUnit(int sortKey, Entity prefab, float3 spawnPos, int ownerId)
        {
            if (prefab == Entity.Null) return;

            // Instantiate
            Entity newUnit = Ecb.Instantiate(sortKey, prefab);

            // 위치 설정 (오프셋)
            float3 finalPos = spawnPos + new float3(2f, 0, 2f);
            
            // Transform 설정 (SetComponent)
            Ecb.SetComponent(sortKey, newUnit, LocalTransform.FromPosition(finalPos));
            
            // 소유권 설정
            Ecb.SetComponent(sortKey, newUnit, new GhostOwner { NetworkId = ownerId });
            Ecb.SetComponent(sortKey, newUnit, new Team { teamId = ownerId });
            
            // 필수 컴포넌트가 프리팹에 없는 경우를 대비한 안전장치 (필요 없다면 제거 가능)
            // 성능을 위해 가능하면 프리팹 자체에 컴포넌트를 붙여두는 것이 좋음
            Ecb.AddComponent(sortKey, newUnit, new MoveTarget { position = finalPos, isValid = false });
            Ecb.AddComponent(sortKey, newUnit, new RTSInputState { TargetPosition = float3.zero, HasTarget = false });
        }
    }
}