using Unity.Entities;
using Unity.NetCode;
using Shared;

namespace Server
{
    /// <summary>
    /// 유닛 생산 요청 RPC 처리
    /// - 소유권 검증
    /// - 자원 차감 (TODO)
    /// - ProductionQueue 시작
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial struct HandleProduceUnitRequestSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkStreamInGame>();
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged);

            foreach (var (rpcReceive, rpc, rpcEntity) in
                SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>, RefRO<ProduceUnitRequestRpc>>()
                .WithEntityAccess())
            {
                ProcessRequest(ref state, ecb, rpcReceive.ValueRO, rpc.ValueRO);
                ecb.DestroyEntity(rpcEntity);
            }
        }

        private void ProcessRequest(
            ref SystemState state,
            EntityCommandBuffer ecb,
            ReceiveRpcCommandRequest rpcReceive,
            ProduceUnitRequestRpc rpc)
        {
            // 1. Ghost ID로 배럭 엔티티 찾기
            Entity barracksEntity = FindEntityByGhostId(ref state, rpc.BarracksGhostId);
            if (barracksEntity == Entity.Null)
            {
                UnityEngine.Debug.LogWarning($"[HandleProduceUnitRequest] Barracks not found for GhostId: {rpc.BarracksGhostId}");
                return;
            }

            // 2. 소유권 검증
            if (!ValidateOwnership(ref state, barracksEntity, rpcReceive.SourceConnection))
            {
                UnityEngine.Debug.LogWarning("[HandleProduceUnitRequest] Ownership validation failed");
                return;
            }

            // 3. BarracksTag 및 ProductionQueue 확인
            if (!state.EntityManager.HasComponent<BarracksTag>(barracksEntity) ||
                !state.EntityManager.HasComponent<ProductionQueue>(barracksEntity))
            {
                UnityEngine.Debug.LogWarning("[HandleProduceUnitRequest] Entity is not a valid barracks");
                return;
            }

            // 4. 이미 생산 중인지 확인
            var productionQueue = state.EntityManager.GetComponentData<ProductionQueue>(barracksEntity);
            if (productionQueue.IsActive)
            {
                UnityEngine.Debug.Log("[HandleProduceUnitRequest] Already producing");
                return;
            }

            // 5. 생산 가능 유닛 버퍼에서 프리팹 찾기
            if (!state.EntityManager.HasBuffer<ProducibleUnitElement>(barracksEntity))
            {
                UnityEngine.Debug.LogWarning("[HandleProduceUnitRequest] No ProducibleUnitElement buffer");
                return;
            }

            var unitBuffer = state.EntityManager.GetBuffer<ProducibleUnitElement>(barracksEntity);
            if (rpc.UnitIndex < 0 || rpc.UnitIndex >= unitBuffer.Length)
            {
                UnityEngine.Debug.LogWarning($"[HandleProduceUnitRequest] Invalid UnitIndex: {rpc.UnitIndex}");
                return;
            }

            Entity unitPrefab = unitBuffer[rpc.UnitIndex].PrefabEntity;
            if (unitPrefab == Entity.Null)
            {
                UnityEngine.Debug.LogWarning("[HandleProduceUnitRequest] Unit prefab is null");
                return;
            }

            // 6. 자원 확인 및 차감 (TODO: UserResources 시스템 연동)
            // if (state.EntityManager.HasComponent<ProductionCost>(unitPrefab))
            // {
            //     var cost = state.EntityManager.GetComponentData<ProductionCost>(unitPrefab);
            //     // Economy.TryDeductResources(...) 호출
            // }

            // 7. 생산 시간 가져오기
            float duration = 5f; // 기본값
            if (state.EntityManager.HasComponent<ProductionInfo>(unitPrefab))
            {
                duration = state.EntityManager.GetComponentData<ProductionInfo>(unitPrefab).ProductionTime;
            }

            // 8. ProductionQueue 시작 (인덱스 저장 - Entity ID는 서버/클라이언트 간 불일치)
            ecb.SetComponent(barracksEntity, new ProductionQueue
            {
                ProducingUnitIndex = rpc.UnitIndex,
                Progress = 0,
                Duration = duration,
                IsActive = true
            });

            UnityEngine.Debug.Log($"[HandleProduceUnitRequest] Started production, Duration: {duration}s");
        }

        private Entity FindEntityByGhostId(ref SystemState state, int ghostId)
        {
            foreach (var (ghost, entity) in
                SystemAPI.Query<RefRO<GhostInstance>>().WithEntityAccess())
            {
                if (ghost.ValueRO.ghostId == ghostId)
                    return entity;
            }
            return Entity.Null;
        }

        private bool ValidateOwnership(ref SystemState state, Entity target, Entity sourceConnection)
        {
            if (!state.EntityManager.HasComponent<GhostOwner>(target)) return false;
            if (!state.EntityManager.HasComponent<NetworkId>(sourceConnection)) return false;

            var owner = state.EntityManager.GetComponentData<GhostOwner>(target);
            var networkId = state.EntityManager.GetComponentData<NetworkId>(sourceConnection);

            return owner.NetworkId == networkId.Value;
        }
    }
}
