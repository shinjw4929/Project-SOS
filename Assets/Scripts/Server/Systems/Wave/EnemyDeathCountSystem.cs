using Unity.Burst;
using Unity.Entities;
using Unity.NetCode;
using Shared;

namespace Server
{
    /// <summary>
    /// 적 처치 수 카운팅 시스템.
    /// ServerDeathSystem에서 파괴되기 전에 EnemyTag 엔티티를 감지하여 카운트.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(ServerDeathSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial struct EnemyDeathCountSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GamePhaseState>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingletonEntity<GamePhaseState>(out Entity phaseStateEntity))
                return;

            var phaseState = SystemAPI.GetSingleton<GamePhaseState>();
            int killCount = 0;

            // Health <= 0인 EnemyTag 엔티티 카운트
            foreach (var (health, _) in
                SystemAPI.Query<RefRO<Health>, RefRO<EnemyTag>>())
            {
                if (health.ValueRO.CurrentValue <= 0)
                {
                    killCount++;
                }
            }

            if (killCount > 0)
            {
                phaseState.TotalKillCount += killCount;
                SystemAPI.SetComponent(phaseStateEntity, phaseState);
            }
        }
    }
}
