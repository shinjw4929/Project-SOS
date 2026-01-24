using Unity.Entities;
using Unity.NetCode;
using Unity.Collections;
using Unity.Burst;
using Shared;

namespace Server
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [BurstCompile]
    public partial struct HandleProduceUnitRequestSystem : ISystem
    {
        // 1. 모든 컴포넌트 접근을 Lookup으로 전환
        [ReadOnly] private ComponentLookup<ProductionCost> _productionCostLookup;
        [ReadOnly] private ComponentLookup<ProductionInfo> _productionInfoLookup;
        [ReadOnly] private ComponentLookup<GhostOwner> _ghostOwnerLookup;
        [ReadOnly] private ComponentLookup<NetworkId> _networkIdLookup;
        [ReadOnly] private ComponentLookup<ProductionFacilityTag> _facilityTagLookup;
        
        // 쓰기 권한이 필요한 Lookup (ReadOnly 제거)
        private ComponentLookup<UserCurrency> _userCurrencyLookup;
        private ComponentLookup<UserSupply> _userSupplyLookup;
        private ComponentLookup<ProductionQueue> _productionQueueLookup;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkStreamInGame>();
            state.RequireForUpdate<UnitCatalog>();
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
            state.RequireForUpdate<GhostIdMap>();

            // Lookup 초기화 (ReadOnly 여부 주의)
            _productionCostLookup = state.GetComponentLookup<ProductionCost>(true);
            _productionInfoLookup = state.GetComponentLookup<ProductionInfo>(true);
            _ghostOwnerLookup = state.GetComponentLookup<GhostOwner>(true);
            _networkIdLookup = state.GetComponentLookup<NetworkId>(true);
            _facilityTagLookup = state.GetComponentLookup<ProductionFacilityTag>(true);
            
            _userCurrencyLookup = state.GetComponentLookup<UserCurrency>(false); // Write
            _userSupplyLookup = state.GetComponentLookup<UserSupply>(false); // Write
            _productionQueueLookup = state.GetComponentLookup<ProductionQueue>(false); // Write
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // 2. Lookup 갱신 (매 프레임 필수)
            _productionCostLookup.Update(ref state);
            _productionInfoLookup.Update(ref state);
            _ghostOwnerLookup.Update(ref state);
            _networkIdLookup.Update(ref state);
            _facilityTagLookup.Update(ref state);
            _userCurrencyLookup.Update(ref state);
            _userSupplyLookup.Update(ref state);
            _productionQueueLookup.Update(ref state);

            var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged);

            var catalogEntity = SystemAPI.GetSingletonEntity<UnitCatalog>();
            var prefabBuffer = SystemAPI.GetBuffer<UnitCatalogElement>(catalogEntity);

            // 3. GhostIdMap 싱글톤 재사용 (GhostIdLookupSystem이 매 프레임 갱신)
            var ghostMap = SystemAPI.GetSingleton<GhostIdMap>().Map;

            // 4. Currency Map 생성 (Allocator.Temp)
            var networkIdToCurrencyEntity = new NativeParallelHashMap<int, Entity>(16, Allocator.Temp);
            foreach (var (ghostOwner, entity) in SystemAPI.Query<RefRO<GhostOwner>>()
                         .WithAll<UserEconomyTag>()
                         .WithEntityAccess())
            {
                networkIdToCurrencyEntity.TryAdd(ghostOwner.ValueRO.NetworkId, entity);
            }
            
            // 5. RPC 처리
            foreach (var (rpcReceive, rpc, rpcEntity) in
                SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>, RefRO<ProduceUnitRequestRpc>>()
                .WithEntityAccess())
            {
                // GhostMap 검색
                if (ghostMap.TryGetValue(rpc.ValueRO.StructureGhostId, out Entity producerEntity))
                {
                    ProcessRequest(
                        ecb, 
                        producerEntity, 
                        rpcReceive.ValueRO.SourceConnection, 
                        rpc.ValueRO, 
                        prefabBuffer, 
                        networkIdToCurrencyEntity
                    );
                }
                
                ecb.DestroyEntity(rpcEntity);
            }
            
            // Allocator.Temp는 함수 종료 시 자동 해제되지만, 명시적 Dispose는 좋은 습관 (여기선 자동 처리됨)
        }

        private void ProcessRequest(
            EntityCommandBuffer ecb,
            Entity producerEntity,
            Entity sourceConnection,
            ProduceUnitRequestRpc rpc,
            DynamicBuffer<UnitCatalogElement> prefabBuffer,
            NativeParallelHashMap<int, Entity> networkIdToCurrencyMap)
        {
            // 1. 소유권 및 컴포넌트 존재 여부 검증 (Lookup 사용)
            if (!_ghostOwnerLookup.HasComponent(producerEntity) || 
                !_networkIdLookup.HasComponent(sourceConnection) ||
                !_facilityTagLookup.HasComponent(producerEntity) ||
                !_productionQueueLookup.HasComponent(producerEntity)) 
                return;

            int ownerId = _ghostOwnerLookup[producerEntity].NetworkId;
            int requesterId = _networkIdLookup[sourceConnection].Value;

            if (ownerId != requesterId) return;

            // 2. 생산 중인지 확인 (RefRW로 접근하여 복사 방지)
            RefRW<ProductionQueue> queueRW = _productionQueueLookup.GetRefRW(producerEntity);
            if (queueRW.ValueRO.IsActive) return;

            // 3. 프리팹 확인
            if (rpc.UnitIndex < 0 || rpc.UnitIndex >= prefabBuffer.Length) return;
            Entity unitPrefab = prefabBuffer[rpc.UnitIndex].PrefabEntity;
            if (unitPrefab == Entity.Null) return;

            // 4. 자원 및 인구수 비용 확인
            if (!_productionCostLookup.HasComponent(unitPrefab)) return;
            var productionCost = _productionCostLookup[unitPrefab];
            int constructionCost = productionCost.Cost;
            int populationCost = productionCost.PopulationCost;

            if (networkIdToCurrencyMap.TryGetValue(ownerId, out Entity userCurrencyEntity))
            {
                RefRW<UserCurrency> currencyRW = _userCurrencyLookup.GetRefRW(userCurrencyEntity);

                // 4-1. 자원 부족 검사
                if (currencyRW.ValueRO.Amount < constructionCost)
                {
                    // 자원 부족 알림 RPC 전송
                    if (sourceConnection != Entity.Null)
                    {
                        var notifyEntity = ecb.CreateEntity();
                        ecb.AddComponent(notifyEntity, new NotificationRpc { Type = NotificationType.InsufficientFunds });
                        ecb.AddComponent(notifyEntity, new SendRpcCommandRequest { TargetConnection = sourceConnection });
                    }
                    return;
                }

                // 4-2. 인구수 검사 및 예약
                if (_userSupplyLookup.HasComponent(userCurrencyEntity))
                {
                    RefRW<UserSupply> supplyRW = _userSupplyLookup.GetRefRW(userCurrencyEntity);

                    if (!supplyRW.ValueRO.CanProduce(populationCost))
                    {
                        // 인구수 초과 알림 RPC 전송
                        if (sourceConnection != Entity.Null)
                        {
                            var notifyEntity = ecb.CreateEntity();
                            ecb.AddComponent(notifyEntity, new NotificationRpc { Type = NotificationType.PopulationLimitReached });
                            ecb.AddComponent(notifyEntity, new SendRpcCommandRequest { TargetConnection = sourceConnection });
                        }
                        return;
                    }

                    // 5. [최종 승인] 자원 차감 + 인구수 즉시 증가 (예약)
                    currencyRW.ValueRW.Amount -= constructionCost;
                    supplyRW.ValueRW.Currentvalue += populationCost;
                }
                else
                {
                    // UserSupply 없으면 자원만 차감
                    currencyRW.ValueRW.Amount -= constructionCost;
                }
            }
            else
            {
                return; // 자원 엔티티 못 찾음
            }
            
            // 6. 생산 시간 조회 (Lookup)
            float duration = 5f;
            if (_productionInfoLookup.HasComponent(unitPrefab))
            {
                duration = _productionInfoLookup[unitPrefab].ProductionTime;
            }

            // 7. 생산 시작 (Queue 직접 수정)
            queueRW.ValueRW.ProducingUnitIndex = rpc.UnitIndex;
            queueRW.ValueRW.Progress = 0;
            queueRW.ValueRW.Duration = duration;
            queueRW.ValueRW.IsActive = true;
            
            // ComponentLookup.GetRefRW를 사용했으므로 ecb.SetComponent 불필요!
            // 이미 메인 스레드(혹은 잡)에서 컴포넌트 데이터 원본을 수정했습니다.
            // *주의*: 만약 이 시스템이 병렬 Job이라면 RefRW 사용 시 경합이 발생할 수 있으나, 
            // 현재 구조는 MainThread OnUpdate이므로 안전하며 가장 빠릅니다.
        }
    }
}