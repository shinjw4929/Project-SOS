using Shared;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Systems;

namespace Server
{
    /// <summary>
    /// 투사체 데미지 시스템
    /// - Physics Trigger 기반 충돌 감지
    /// - DamageEvent 버퍼에 데미지 추가 (DamageApplySystem에서 적용)
    /// </summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
    [UpdateAfter(typeof(PhysicsSimulationGroup))]
    public partial struct CombatDamageSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<SimulationSingleton>();
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var simulation = SystemAPI.GetSingleton<SimulationSingleton>();
            var ecbSingleton = SystemAPI.GetSingletonRW<EndSimulationEntityCommandBufferSystem.Singleton>();

            // 데이터 조회를 위한 Lookup 초기화 (모두 읽기 전용)
            var healthLookup = SystemAPI.GetComponentLookup<Health>(true);
            var defenseLookup = SystemAPI.GetComponentLookup<Defense>(true);
            var projectileTagLookup = SystemAPI.GetComponentLookup<ProjectileTag>(true);
            var combatStatusLookup = SystemAPI.GetComponentLookup<CombatStats>(true);
            var teamLookup = SystemAPI.GetComponentLookup<Team>(true);
            var damageEventLookup = SystemAPI.GetBufferLookup<DamageEvent>(true);

            var commandBuffer = ecbSingleton.ValueRW.CreateCommandBuffer(state.WorldUnmanaged);

            // 동일 프레임 내 중복 처리 방지를 위한 집합
            var processedProjectiles = new NativeParallelHashSet<Entity>(1024, Allocator.TempJob);

            var damageJob = new CombatTriggerJob
            {
                HealthLookup = healthLookup,
                DefenseLookup = defenseLookup,
                ProjectileTagLookup = projectileTagLookup,
                CombatStatusLookup = combatStatusLookup,
                TeamLookup = teamLookup,
                DamageEventLookup = damageEventLookup,
                CommandBuffer = commandBuffer,
                ProcessedProjectiles = processedProjectiles.AsParallelWriter()
            };

            state.Dependency = damageJob.Schedule(simulation, state.Dependency);

            // HashSet 할당 해제 예약
            processedProjectiles.Dispose(state.Dependency);
        }

        [BurstCompile]
        private struct CombatTriggerJob : ITriggerEventsJob
        {
            [ReadOnly] public ComponentLookup<Health> HealthLookup;
            [ReadOnly] public ComponentLookup<Defense> DefenseLookup;
            [ReadOnly] public ComponentLookup<ProjectileTag> ProjectileTagLookup;
            [ReadOnly] public ComponentLookup<CombatStats> CombatStatusLookup;
            [ReadOnly] public ComponentLookup<Team> TeamLookup;
            [ReadOnly] public BufferLookup<DamageEvent> DamageEventLookup;

            public EntityCommandBuffer CommandBuffer;
            public NativeParallelHashSet<Entity>.ParallelWriter ProcessedProjectiles;

            public void Execute(TriggerEvent triggerEvent)
            {
                Entity entityA = triggerEvent.EntityA;
                Entity entityB = triggerEvent.EntityB;

                // 1. 투사체가 포함되어 있는지 확인
                bool isAProjectile = ProjectileTagLookup.HasComponent(entityA);
                bool isBProjectile = ProjectileTagLookup.HasComponent(entityB);

                if (!isAProjectile && !isBProjectile) return;

                // 2. 공격자(투사체)와 피격자(대상) 구분
                Entity projectileEntity = isAProjectile ? entityA : entityB;
                Entity victimEntity = isAProjectile ? entityB : entityA;

                // 3. 필수 컴포넌트 존재 여부 확인
                if (!CombatStatusLookup.HasComponent(projectileEntity)) return;
                if (!HealthLookup.HasComponent(victimEntity)) return;
                if (!DamageEventLookup.HasBuffer(victimEntity)) return;

                // 4. 아군 히트 방지
                if (TeamLookup.HasComponent(projectileEntity) && TeamLookup.HasComponent(victimEntity))
                {
                    int teamA = TeamLookup[projectileEntity].teamId;
                    int teamB = TeamLookup[victimEntity].teamId;

                    // 같은 팀이면 무시
                    if (teamA == teamB) return;
                }

                // 5. 다중 히트 방지 (투사체가 한 프레임에 여러 물체와 부딪힐 경우)
                if (!ProcessedProjectiles.Add(projectileEntity)) return;

                // 6. 데미지 계산
                float baseDamage = CombatStatusLookup[projectileEntity].AttackPower;

                float defenseValue = DefenseLookup.HasComponent(victimEntity)
                    ? DefenseLookup[victimEntity].Value
                    : 0f;

                float finalDamage = DamageUtility.CalculateDamage(baseDamage, defenseValue);

                // 7. DamageEvent 버퍼에 데미지 추가 (ECB 사용)
                CommandBuffer.AppendToBuffer(victimEntity, new DamageEvent { Damage = finalDamage });

                // 8. 투사체 엔티티 제거
                CommandBuffer.DestroyEntity(projectileEntity);
            }
        }
    }
}
