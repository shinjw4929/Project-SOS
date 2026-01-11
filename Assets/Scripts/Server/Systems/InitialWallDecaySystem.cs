using Unity.Burst;
using Unity.Entities;
using Unity.NetCode;
using Shared;

namespace Server
{
    /// <summary>
    /// 초기 배치 벽 자동 파괴 시스템.
    /// InitialWallTag가 있는 벽에 타이머를 추가하고, 시간이 지나면 파괴.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial struct InitialWallDecaySystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GameSettings>();
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var gameSettings = SystemAPI.GetSingleton<GameSettings>();
            float deltaTime = SystemAPI.Time.DeltaTime;
            var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged);

            // Phase 1: 타이머가 없는 초기 벽에 타이머 추가
            foreach (var (_, entity) in
                SystemAPI.Query<RefRO<InitialWallTag>>()
                    .WithNone<InitialWallDecayTimer>()
                    .WithEntityAccess())
            {
                ecb.AddComponent(entity, new InitialWallDecayTimer
                {
                    RemainingTime = gameSettings.InitialWallDecayTime
                });
            }

            // Phase 2: 타이머 업데이트 및 파괴
            foreach (var (timer, entity) in
                SystemAPI.Query<RefRW<InitialWallDecayTimer>>()
                    .WithEntityAccess())
            {
                timer.ValueRW.RemainingTime -= deltaTime;

                if (timer.ValueRO.RemainingTime <= 0)
                {
                    ecb.DestroyEntity(entity);
                }
            }
        }
    }
}
