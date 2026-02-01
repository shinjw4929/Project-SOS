using Shared;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

namespace Server
{
    /// <summary>
    /// 데미지 적용 시스템
    /// - DamageEvent 버퍼의 데미지를 Health에 적용
    /// - 적 사망 시 GamePhaseState.TotalKillCount 갱신 (EnemyDeathCountSystem 통합)
    /// - MeleeAttackSystem 이후, ServerDeathSystem 이전에 실행
    /// </summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
    [UpdateAfter(typeof(MeleeAttackSystem))]
    public partial struct DamageApplySystem : ISystem
    {
        private ComponentLookup<EnemyTag> _enemyTagLookup;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _enemyTagLookup = state.GetComponentLookup<EnemyTag>(true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            _enemyTagLookup.Update(ref state);

            int killCount = 0;

            foreach (var (damageBuffer, health, entity) in
                     SystemAPI.Query<DynamicBuffer<DamageEvent>, RefRW<Health>>()
                     .WithEntityAccess())
            {
                if (damageBuffer.Length == 0) continue;

                // 버퍼의 모든 데미지 합산
                float totalDamage = 0f;
                for (int i = 0; i < damageBuffer.Length; i++)
                {
                    totalDamage += damageBuffer[i].Damage;
                }

                // Health에 적용
                float prevHealth = health.ValueRO.CurrentValue;
                health.ValueRW.CurrentValue = math.max(0, prevHealth - totalDamage);

                // 이번 프레임에 사망한 적만 카운트
                if (prevHealth > 0 && health.ValueRO.CurrentValue <= 0 && _enemyTagLookup.HasComponent(entity))
                {
                    killCount++;
                }

                // 버퍼 클리어
                damageBuffer.Clear();
            }

            // GamePhaseState 킬 카운트 갱신
            if (killCount > 0)
            {
                if (SystemAPI.TryGetSingletonEntity<GamePhaseState>(out Entity phaseStateEntity))
                {
                    var phaseState = SystemAPI.GetSingleton<GamePhaseState>();
                    phaseState.TotalKillCount += killCount;
                    SystemAPI.SetComponent(phaseStateEntity, phaseState);
                }
            }
        }
    }
}
