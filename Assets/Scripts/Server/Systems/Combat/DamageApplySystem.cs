using Shared;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

namespace Server
{
    /// <summary>
    /// 데미지 적용 시스템
    /// - DamageEvent 버퍼의 데미지를 Health에 적용
    /// - MeleeAttackSystem 이후, ServerDeathSystem 이전에 실행
    /// </summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
    [UpdateAfter(typeof(MeleeAttackSystem))]
    public partial struct DamageApplySystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            foreach (var (damageBuffer, health) in
                     SystemAPI.Query<DynamicBuffer<DamageEvent>, RefRW<Health>>())
            {
                if (damageBuffer.Length == 0) continue;

                // 버퍼의 모든 데미지 합산
                float totalDamage = 0f;
                for (int i = 0; i < damageBuffer.Length; i++)
                {
                    totalDamage += damageBuffer[i].Damage;
                }

                // Health에 적용
                health.ValueRW.CurrentValue = math.max(0, health.ValueRO.CurrentValue - totalDamage);

                // 버퍼 클리어
                damageBuffer.Clear();
            }
        }
    }
}
