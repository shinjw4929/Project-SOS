using Unity.Burst;
using Unity.Entities;
using Unity.NetCode;

namespace Shared
{
    /// <summary>
    /// 사망 처리 시스템 (예측 실행)
    /// - Health.CurrentValue <= 0인 엔티티 삭제
    /// - 클라이언트/서버 모두에서 실행되어 반응성 향상
    /// </summary>
    [UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
    [BurstCompile]
    public partial struct DeathSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

            foreach (var (health, entity) in
                SystemAPI.Query<RefRO<Health>>()
                .WithAll<Simulate>()
                .WithEntityAccess())
            {
                if (health.ValueRO.CurrentValue <= 0)
                {
                    ecb.DestroyEntity(entity);
                }
            }
            // ECB는 시스템에서 자동으로 Playback/Dispose됨
        }
    }
}
