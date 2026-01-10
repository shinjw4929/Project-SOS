using Unity.Burst;
using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;
using Unity.Mathematics;
using Shared;

namespace Server
{
    /// <summary>
    /// 자폭 타이머 처리
    /// SelfDestructTag의 RemainingTime을 감소시키고, 0이 되면 폭발 실행
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(HandleSelfDestructRequestSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial struct SelfDestructTimerSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        }

        public void OnUpdate(ref SystemState state)
        {
            float deltaTime = SystemAPI.Time.DeltaTime;
            var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged);

            foreach (var (selfDestruct, explosionData, entity) in
                SystemAPI.Query<RefRW<SelfDestructTag>, RefRO<ExplosionData>>()
                .WithEntityAccess())
            {
                // RemainingTime < 0이면 자폭 대기 아님 (기본값 -1)
                if (selfDestruct.ValueRO.RemainingTime < 0) continue;

                selfDestruct.ValueRW.RemainingTime -= deltaTime;

                if (selfDestruct.ValueRW.RemainingTime <= 0)
                {
                    ExecuteExplosion(ref state, ecb, entity, explosionData.ValueRO);
                }
            }
        }

        private void ExecuteExplosion(
            ref SystemState state,
            EntityCommandBuffer ecb,
            Entity sourceEntity,
            ExplosionData explosionData)
        {
            var position = state.EntityManager.GetComponentData<LocalTransform>(sourceEntity).Position;
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
