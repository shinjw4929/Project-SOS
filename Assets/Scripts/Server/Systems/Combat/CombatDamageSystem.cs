using Shared;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Systems;

namespace Server
{
    /// <summary>
    /// 통합 전투 데미지 시스템 (투사체 + 근접 공격)
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
            
            // 데이터 조회를 위한 Lookup 초기화
            var healthLookup = SystemAPI.GetComponentLookup<Health>(false);
            var defenseLookup = SystemAPI.GetComponentLookup<Defense>(true);
            var projectileTagLookup = SystemAPI.GetComponentLookup<ProjectileTag>(true);
            var combatStatusLookup = SystemAPI.GetComponentLookup<CombatStats>(true);
            var teamLookup = SystemAPI.GetComponentLookup<Team>(true);
            
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
            public ComponentLookup<Health> HealthLookup;
            [ReadOnly] public ComponentLookup<Defense> DefenseLookup;
            [ReadOnly] public ComponentLookup<ProjectileTag> ProjectileTagLookup;
            [ReadOnly] public ComponentLookup<CombatStats> CombatStatusLookup;
            [ReadOnly] public ComponentLookup<Team> TeamLookup;
            
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

                // 4. 아군 히트 방지
                if (TeamLookup.HasComponent(projectileEntity) && TeamLookup.HasComponent(victimEntity))
                {
                    int teamA = TeamLookup[projectileEntity].teamId;
                    int teamB = TeamLookup[victimEntity].teamId;

                    // 같은 팀이면 무시 (단, 서로 다른 팀이거나 하나가 중립이면 데미지 허용 등 기획에 따라 조정)
                    if (teamA == teamB) return;
                }
                
                // 5. 다중 히트 방지 (투사체가 한 프레임에 여러 물체와 부딪힐 경우)
                if (!ProcessedProjectiles.Add(projectileEntity)) return;

                // 6. 데미지 계산 및 적용
                var health = HealthLookup[victimEntity];
                float baseDamage = CombatStatusLookup[projectileEntity].AttackPower;

                float defenseValue = DefenseLookup.HasComponent(victimEntity)
                    ? DefenseLookup[victimEntity].Value
                    : 0f;

                float finalDamage = DamageUtility.CalculateDamage(baseDamage, defenseValue);

                health.CurrentValue = math.max(0, health.CurrentValue - finalDamage);
                HealthLookup[victimEntity] = health;

                // 7. 투사체 엔티티 제거
                CommandBuffer.DestroyEntity(projectileEntity);
            }
        }
    }
}
