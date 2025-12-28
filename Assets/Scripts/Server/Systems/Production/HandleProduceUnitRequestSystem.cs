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
        // [GhostID -> Entity] 빠른 검색을 위한 맵
        private NativeParallelHashMap<int, Entity> _ghostMap;

        public void OnCreate(ref SystemState state)
        {
            _ghostMap = new NativeParallelHashMap<int, Entity>(128, Allocator.Persistent);
            
            state.RequireForUpdate<NetworkStreamInGame>();
            state.RequireForUpdate<UnitCatalog>();
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        }

        public void OnDestroy(ref SystemState state)
        {
            if (_ghostMap.IsCreated) _ghostMap.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // 1. GhostMap 갱신 (매 프레임 혹은 GhostCount가 변할 때만)
            // 여기서는 간단하게 매 프레임 새로 빌드하지만, 최적화하려면 ChangeFilter 등을 고려 가능
            // 하지만 서버에서 GhostEntity 개수가 수천 개가 아니라면 이 방식도 충분히 빠름
            BuildGhostMap(ref state);

            var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged);

            var catalogEntity = SystemAPI.GetSingletonEntity<UnitCatalog>();
            var prefabBuffer = SystemAPI.GetBuffer<UnitCatalogElement>(catalogEntity);

            // RPC 처리
            foreach (var (rpcReceive, rpc, rpcEntity) in
                SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>, RefRO<ProduceUnitRequestRpc>>()
                .WithEntityAccess())
            {
                // [최적화] 함수 호출 대신 인라인 혹은 간단한 처리
                if (_ghostMap.TryGetValue(rpc.ValueRO.StructureGhostId, out Entity producerEntity))
                {
                    ProcessRequest(ref state, ecb, producerEntity, rpcReceive.ValueRO.SourceConnection, rpc.ValueRO, prefabBuffer);
                }
                
                // RPC 엔티티 삭제
                ecb.DestroyEntity(rpcEntity);
            }
        }

        private void BuildGhostMap(ref SystemState state)
        {
            _ghostMap.Clear();
            foreach (var (ghost, entity) in SystemAPI.Query<RefRO<GhostInstance>>().WithEntityAccess())
            {
                _ghostMap.TryAdd(ghost.ValueRO.ghostId, entity);
            }
        }

        private void ProcessRequest(
            ref SystemState state,
            EntityCommandBuffer ecb,
            Entity producerEntity,
            Entity sourceConnection,
            ProduceUnitRequestRpc rpc,
            DynamicBuffer<UnitCatalogElement> prefabBuffer)
        {
            // 1. 소유권 검증 (ComponentDataFromEntity 사용 가능)
            if (!SystemAPI.HasComponent<GhostOwner>(producerEntity) || 
                !SystemAPI.HasComponent<NetworkId>(sourceConnection)) return;

            var ownerId = SystemAPI.GetComponent<GhostOwner>(producerEntity).NetworkId;
            var requesterId = SystemAPI.GetComponent<NetworkId>(sourceConnection).Value;

            if (ownerId != requesterId) return;

            // 2. 컴포넌트 검증 (ProductionFacility, Queue)
            if (!SystemAPI.HasComponent<ProductionFacilityTag>(producerEntity)) return;

            // 3. 생산 중인지 확인
            // RefRW 대신 값을 직접 가져와서 확인 (Queue는 수정할 것이므로 나중에 Set)
            var queue = SystemAPI.GetComponent<ProductionQueue>(producerEntity);
            if (queue.IsActive) return;

            // 4. 프리팹 인덱스 확인
            if (rpc.UnitIndex < 0 || rpc.UnitIndex >= prefabBuffer.Length) return;

            Entity unitPrefab = prefabBuffer[rpc.UnitIndex].PrefabEntity;
            if (unitPrefab == Entity.Null) return;

            // 5. 생산 시간 조회
            float duration = 5f;
            if (SystemAPI.HasComponent<ProductionInfo>(unitPrefab))
            {
                duration = SystemAPI.GetComponent<ProductionInfo>(unitPrefab).ProductionTime;
            }

            // 6. 생산 시작 (Queue 업데이트)
            queue.ProducingUnitIndex = rpc.UnitIndex;
            queue.Progress = 0;
            queue.Duration = duration;
            queue.IsActive = true;

            // 변경된 Queue를 다시 설정
            ecb.SetComponent(producerEntity, queue);
        }
    }
}