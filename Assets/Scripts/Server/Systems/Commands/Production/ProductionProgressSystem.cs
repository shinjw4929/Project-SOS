#if LEGACY_MOVEMENT_SYSTEM
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
        // 1. 시스템 필드에 Lookup 선언
        [ReadOnly] private ComponentLookup<LocalTransform> _transformLookup;
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
            state.RequireForUpdate<UnitCatalog>();
            // 2. Lookup 초기화 (읽기 전용)
            _transformLookup = state.GetComponentLookup<LocalTransform>(isReadOnly: true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // 3. 매 프레임 Lookup 갱신 (필수!)
            _transformLookup.Update(ref state);
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
                Ecb = ecb,
                TransformLookup = _transformLookup // 4. Job에 Lookup 전달
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
        [ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;

        // 필요한 컴포넌트만 ref로 가져옴
        private void Execute(
            [EntityIndexInQuery] int sortKey, 
            Entity entity, 
            ref ProductionQueue queue, 
            in LocalTransform transform, 
            in GhostOwner owner,
            in StructureFootprint footprint)
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
                    
                    
                    float xOffset = (footprint.Width * 0.5f) + 1.0f;
                    float zOffset = (footprint.Length * 0.5f) + 1.0f;
                    float3 spawnPos = new float3(
                        transform.Position.x + xOffset, 
                        0f, 
                        transform.Position.z - zOffset
                    );
                    
                    // 유닛 스폰 명령 예약
                    SpawnUnit(sortKey, prefab, spawnPos, owner.NetworkId);
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

            // A. 유닛 생성
            Entity newUnit = Ecb.Instantiate(sortKey, prefab);
            
            // B. 프리팹의 Transform 정보 가져오기
            // TransformLookup.HasComponent(Entity)로 안전하게 확인
            if (TransformLookup.HasComponent(prefab))
            {
                // 프리팹의 원본 Transform 복사 (여기에 Scale 값이 들어있음!)
                LocalTransform prefabTransform = TransformLookup[prefab];
                
                // 위치만 변경
                prefabTransform.Position += spawnPos;
                
                // 적용
                Ecb.SetComponent(sortKey, newUnit, prefabTransform);
            }
            else
            {
                // 프리팹에 Transform이 없는 경우 (예외 처리)
                Ecb.SetComponent(sortKey, newUnit, LocalTransform.FromPosition(spawnPos));
            }
            
            // 소유권 설정
            Ecb.SetComponent(sortKey, newUnit, new GhostOwner { NetworkId = ownerId });
            Ecb.SetComponent(sortKey, newUnit, new Team { teamId = ownerId });
            
            // 필수 컴포넌트가 프리팹에 없는 경우를 대비한 안전장치 (필요 없다면 제거 가능)
            // 성능을 위해 가능하면 프리팹 자체에 컴포넌트를 붙여두는 것이 좋음
            Ecb.AddComponent(sortKey, newUnit, new MovementDestination { Position = spawnPos, IsValid = false });
            Ecb.AddComponent(sortKey, newUnit, new UnitInputData { TargetPosition = float3.zero, HasTarget = false });
        }
    }
}
#endif