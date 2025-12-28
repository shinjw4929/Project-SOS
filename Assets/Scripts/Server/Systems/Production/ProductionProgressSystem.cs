using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;
using Unity.Mathematics;
using Shared;

namespace Server
{
    /// <summary>
    /// 생산 진행도 업데이트 (서버만)
    /// - Progress 증가
    /// - 완료 시 유닛 스폰
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial struct ProductionProgressSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        }

        public void OnUpdate(ref SystemState state)
        {
            float deltaTime = SystemAPI.Time.DeltaTime;
            var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged);

            foreach (var (productionQueue, unitBuffer, transform, owner, entity) in
                SystemAPI.Query<RefRW<ProductionQueue>, DynamicBuffer<UnitCatalogElement>, RefRO<LocalTransform>, RefRO<GhostOwner>>()
                .WithAll<BarracksTag>()
                .WithEntityAccess())
            {
                if (!productionQueue.ValueRO.IsActive) continue;

                // Progress 업데이트
                productionQueue.ValueRW.Progress += deltaTime;

                // 완료 체크
                if (productionQueue.ValueRO.Progress >= productionQueue.ValueRO.Duration)
                {
                    int unitIndex = productionQueue.ValueRO.ProducingUnitIndex;

                    // 인덱스로 프리팹 조회
                    if (unitIndex >= 0 && unitIndex < unitBuffer.Length)
                    {
                        Entity prefab = unitBuffer[unitIndex].PrefabEntity;

                        UnityEngine.Debug.Log($"[ProductionProgress] UnitIndex: {unitIndex}, BufferLength: {unitBuffer.Length}, Prefab: {prefab}");

                        // 프리팹 유효성 검사
                        if (prefab == Entity.Null)
                        {
                            UnityEngine.Debug.LogError($"[ProductionProgress] Prefab at index {unitIndex} is Entity.Null!");
                            continue;
                        }

                        if (!state.EntityManager.Exists(prefab))
                        {
                            UnityEngine.Debug.LogError($"[ProductionProgress] Prefab {prefab} does not exist in EntityManager!");
                            continue;
                        }

                        // Ghost 프리팹인지 확인
                        bool hasGhostInstance = state.EntityManager.HasComponent<GhostInstance>(prefab);
                        bool hasPrefabTag = state.EntityManager.HasComponent<Unity.Entities.Prefab>(prefab);
                        UnityEngine.Debug.Log($"[ProductionProgress] Prefab check - HasGhostInstance: {hasGhostInstance}, HasPrefab: {hasPrefabTag}");

                        // 유닛 스폰
                        SpawnUnit(ref state, ecb,
                            prefab,
                            transform.ValueRO.Position,
                            owner.ValueRO.NetworkId);
                    }
                    else
                    {
                        UnityEngine.Debug.LogWarning($"[ProductionProgress] Invalid unit index: {unitIndex}");
                    }

                    // Queue 초기화
                    productionQueue.ValueRW = new ProductionQueue
                    {
                        ProducingUnitIndex = -1,
                        Progress = 0,
                        Duration = 0,
                        IsActive = false
                    };

                    UnityEngine.Debug.Log("[ProductionProgress] Unit production completed!");
                }
            }
        }

        private void SpawnUnit(
            ref SystemState state,
            EntityCommandBuffer ecb,
            Entity prefab,
            float3 barracksPosition,
            int ownerId)
        {
            if (prefab == Entity.Null || !state.EntityManager.Exists(prefab))
            {
                UnityEngine.Debug.LogWarning("[ProductionProgress] Invalid prefab, cannot spawn unit");
                return;
            }

            Entity newUnit = ecb.Instantiate(prefab);

            // 배럭 근처에 스폰 (오프셋 적용)
            float3 spawnPos = barracksPosition + new float3(2f, 0, 2f);

            // 프리팹의 Transform을 복사해서 위치만 변경
            if (state.EntityManager.HasComponent<LocalTransform>(prefab))
            {
                var transformData = state.EntityManager.GetComponentData<LocalTransform>(prefab);
                transformData.Position = spawnPos;
                ecb.SetComponent(newUnit, transformData);
            }
            else
            {
                ecb.SetComponent(newUnit, LocalTransform.FromPosition(spawnPos));
            }

            // 소유자 설정 (프리팹에 이미 있으면 Set, 없으면 Add)
            if (state.EntityManager.HasComponent<GhostOwner>(prefab))
            {
                ecb.SetComponent(newUnit, new GhostOwner { NetworkId = ownerId });
            }
            else
            {
                ecb.AddComponent(newUnit, new GhostOwner { NetworkId = ownerId });
            }

            if (state.EntityManager.HasComponent<Team>(prefab))
            {
                ecb.SetComponent(newUnit, new Team { teamId = ownerId });
            }
            else
            {
                ecb.AddComponent(newUnit, new Team { teamId = ownerId });
            }

            // 이동 관련 컴포넌트 추가 (프리팹에 없을 경우)
            if (!state.EntityManager.HasComponent<MoveTarget>(prefab))
            {
                ecb.AddComponent(newUnit, new MoveTarget { position = spawnPos, isValid = false });
            }

            if (!state.EntityManager.HasBuffer<RTSCommand>(prefab))
            {
                ecb.AddBuffer<RTSCommand>(newUnit);
            }

            if (!state.EntityManager.HasComponent<RTSInputState>(prefab))
            {
                ecb.AddComponent(newUnit, new RTSInputState { TargetPosition = float3.zero, HasTarget = false });
            }

            UnityEngine.Debug.Log($"[ProductionProgress] Spawned unit at {spawnPos} for owner {ownerId}");
        }
    }
}
