using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;
using Unity.Mathematics;
using Shared;

namespace Server
{
    /// <summary>
    /// 자폭 요청 RPC 처리
    /// - Ghost ID로 엔티티 찾기
    /// - 소유권 검증
    /// - SelfDestructTag 부착 (지연 폭발) 또는 즉시 폭발
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial struct HandleSelfDestructRequestSystem : ISystem
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
                SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>, RefRO<SelfDestructRequestRpc>>()
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
            SelfDestructRequestRpc rpc)
        {
            // 1. Ghost ID로 엔티티 찾기
            Entity targetEntity = FindEntityByGhostId(ref state, rpc.TargetGhostId);
            if (targetEntity == Entity.Null)
            {
                UnityEngine.Debug.LogWarning($"[HandleSelfDestructRequest] Entity not found for GhostId: {rpc.TargetGhostId}");
                return;
            }

            // 2. 소유권 검증
            if (!ValidateOwnership(ref state, targetEntity, rpcReceive.SourceConnection))
            {
                UnityEngine.Debug.LogWarning("[HandleSelfDestructRequest] Ownership validation failed");
                return;
            }

            // 3. ExplosionData 확인
            if (!state.EntityManager.HasComponent<ExplosionData>(targetEntity))
            {
                UnityEngine.Debug.LogWarning("[HandleSelfDestructRequest] Entity does not have ExplosionData");
                return;
            }

            // 4. 이미 자폭 중인지 확인 (RemainingTime >= 0이면 자폭 중)
            if (state.EntityManager.HasComponent<SelfDestructTag>(targetEntity))
            {
                var currentTag = state.EntityManager.GetComponentData<SelfDestructTag>(targetEntity);
                if (currentTag.RemainingTime >= 0)
                {
                    UnityEngine.Debug.Log("[HandleSelfDestructRequest] Already self-destructing");
                    return;
                }
            }

            var explosionData = state.EntityManager.GetComponentData<ExplosionData>(targetEntity);

            // 5. 지연 폭발 or 즉시 폭발
            if (explosionData.Delay > 0)
            {
                // 프리팹에 미리 추가된 SelfDestructTag의 값을 변경
                ecb.SetComponent(targetEntity, new SelfDestructTag
                {
                    RemainingTime = explosionData.Delay
                });
                UnityEngine.Debug.Log($"[HandleSelfDestructRequest] Self-destruct initiated with delay: {explosionData.Delay}s");
            }
            else
            {
                // 즉시 폭발 처리
                ExecuteExplosion(ref state, ecb, targetEntity, explosionData);
            }
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

        private void ExecuteExplosion(
            ref SystemState state,
            EntityCommandBuffer ecb,
            Entity sourceEntity,
            ExplosionData explosionData)
        {
            var position = state.EntityManager.GetComponentData<LocalTransform>(sourceEntity).Position;

            UnityEngine.Debug.Log($"[HandleSelfDestructRequest] Explosion at {position}, Radius: {explosionData.Radius}, Damage: {explosionData.Damage}");

            // 범위 내 모든 유닛/건물에 데미지 (사망 처리는 DeathSystem에서)
            foreach (var (health, transform, entity) in
                SystemAPI.Query<RefRW<Health>, RefRO<LocalTransform>>().WithEntityAccess())
            {
                if (entity == sourceEntity) continue;

                float distance = math.distance(position, transform.ValueRO.Position);
                if (distance <= explosionData.Radius)
                {
                    // 거리에 따른 데미지 감쇠
                    float damageFactor = 1f - (distance / explosionData.Radius);
                    float finalDamage = explosionData.Damage * damageFactor;

                    health.ValueRW.CurrentValue -= finalDamage;
                }
            }

            // 자폭한 건물 제거
            ecb.DestroyEntity(sourceEntity);
        }
    }
}
