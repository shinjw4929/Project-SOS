using Shared;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics.Systems;
using Unity.Transforms;

namespace Server
{
    /// <summary>
    /// 근접 공격 시스템
    /// - 유닛과 적 모두 AggroTarget 기반으로 동일하게 처리
    /// - 거리 기반 히트 판정 (RTS 스타일): 직선거리 - 타겟 반지름(ObstacleRadius)
    /// - IJobEntity + Burst로 최적화
    /// - ECB.ParallelWriter를 통한 스레드 안전 데미지 이벤트 추가
    /// </summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
    [UpdateAfter(typeof(PhysicsSystemGroup))]
    public partial struct MeleeAttackSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float deltaTime = SystemAPI.Time.DeltaTime;

            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();

            // 공통 Lookup (읽기 전용) - TeamLookup 제거 (타겟 선정 시스템 신뢰)
            var transformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true);
            var healthLookup = SystemAPI.GetComponentLookup<Health>(true);
            var defenseLookup = SystemAPI.GetComponentLookup<Defense>(true);
            var obstacleRadiusLookup = SystemAPI.GetComponentLookup<ObstacleRadius>(true);

            // =====================================================================
            // 1. 적 유닛 근접 공격 Job
            // =====================================================================
            var enemyJob = new EnemyMeleeAttackJob
            {
                DeltaTime = deltaTime,
                ECB = ecb,
                TransformLookup = transformLookup,
                HealthLookup = healthLookup,
                DefenseLookup = defenseLookup,
                ObstacleRadiusLookup = obstacleRadiusLookup
            };
            state.Dependency = enemyJob.ScheduleParallel(state.Dependency);

            // =====================================================================
            // 2. 아군 유닛 근접 공격 Job
            // NOTE: LocalTransform 쓰기 접근이 겹쳐 순차 실행 필요
            //       (Unity 안전 시스템은 타입 레벨에서 작동, 아키타입 구분 불가)
            // =====================================================================
            var unitJob = new UnitMeleeAttackJob
            {
                DeltaTime = deltaTime,
                ECB = ecb,
                TransformLookup = transformLookup,
                HealthLookup = healthLookup,
                DefenseLookup = defenseLookup,
                ObstacleRadiusLookup = obstacleRadiusLookup
            };
            state.Dependency = unitJob.ScheduleParallel(state.Dependency);
        }
    }

    /// <summary>
    /// 적 근접 공격 Job (원거리 적 제외)
    /// </summary>
    [BurstCompile]
    [WithAll(typeof(EnemyTag))]
    [WithNone(typeof(RangedEnemyTag))]
    public partial struct EnemyMeleeAttackJob : IJobEntity
    {
        public float DeltaTime;
        public EntityCommandBuffer.ParallelWriter ECB;

        // 타겟 엔티티 조회용 (자신과 다른 엔티티이므로 aliasing 안전)
        [ReadOnly, NativeDisableContainerSafetyRestriction]
        public ComponentLookup<LocalTransform> TransformLookup;
        [ReadOnly] public ComponentLookup<Health> HealthLookup;
        [ReadOnly] public ComponentLookup<Defense> DefenseLookup;
        [ReadOnly] public ComponentLookup<ObstacleRadius> ObstacleRadiusLookup;

        private void Execute(
            Entity entity,
            [ChunkIndexInQuery] int sortKey,
            ref LocalTransform transform,
            in AggroTarget aggroTarget,
            in CombatStats combatStats,
            ref AttackCooldown cooldown,
            ref EnemyState enemyState)
        {
            cooldown.RemainingTime = CombatUtility.TickCooldown(cooldown.RemainingTime, DeltaTime);

            Entity targetEntity = aggroTarget.TargetEntity;
            if (targetEntity == Entity.Null) return;

            if (!CombatUtility.IsTargetAlive(targetEntity, in TransformLookup, in HealthLookup, out var targetTransform))
                return;

            float3 targetPos = targetTransform.Position;
            float3 myPos = transform.Position;
            float effectiveDist = CombatUtility.GetEffectiveDistance(myPos, targetPos, targetEntity, in ObstacleRadiusLookup);

            bool isInRange = effectiveDist <= combatStats.AttackRange;

            if (isInRange)
            {
                // 공격 범위 내 (이동 정지)
                if (enemyState.CurrentState != EnemyContext.Attacking)
                {
                    enemyState.CurrentState = EnemyContext.Attacking;
                    ECB.SetComponentEnabled<MovementWaypoints>(sortKey, entity, false);
                }

                CombatUtility.RotateTowardTarget(in myPos, in targetPos, ref transform.Rotation);

                if (cooldown.RemainingTime <= 0)
                {
                    CombatUtility.ApplyDamage(ref ECB, sortKey, targetEntity, entity, combatStats.AttackPower, in DefenseLookup);
                    cooldown.RemainingTime = CombatUtility.ResetCooldown(combatStats.AttackSpeed);
                }
            }
            else
            {
                // 공격 범위 이탈 (추적 재개)
                if (enemyState.CurrentState == EnemyContext.Attacking)
                {
                    enemyState.CurrentState = EnemyContext.Chasing;
                    ECB.SetComponentEnabled<MovementWaypoints>(sortKey, entity, true);

                    // 경로 재계산 트리거
                    ECB.SetComponent(sortKey, entity, new MovementGoal
                    {
                        Destination = targetPos,
                        IsPathDirty = true,
                        CurrentWaypointIndex = 0
                    });
                }
            }
        }
    }

    /// <summary>
    /// 아군 유닛 근접 공격 Job (원거리 유닛 제외)
    /// </summary>
    [BurstCompile]
    [WithAll(typeof(UnitTag))]
    [WithNone(typeof(RangedUnitTag))]
    public partial struct UnitMeleeAttackJob : IJobEntity
    {
        public float DeltaTime;
        public EntityCommandBuffer.ParallelWriter ECB;

        // 타겟 엔티티 조회용 (자신과 다른 엔티티이므로 aliasing 안전)
        [ReadOnly, NativeDisableContainerSafetyRestriction]
        public ComponentLookup<LocalTransform> TransformLookup;
        [ReadOnly] public ComponentLookup<Health> HealthLookup;
        [ReadOnly] public ComponentLookup<Defense> DefenseLookup;
        [ReadOnly] public ComponentLookup<ObstacleRadius> ObstacleRadiusLookup;

        private void Execute(
            Entity entity,
            [ChunkIndexInQuery] int sortKey,
            ref LocalTransform transform,
            ref AggroTarget aggroTarget,
            in CombatStats combatStats,
            ref AttackCooldown cooldown,
            ref UnitIntentState intentState,
            ref UnitActionState actionState)
        {
            cooldown.RemainingTime = CombatUtility.TickCooldown(cooldown.RemainingTime, DeltaTime);

            // Intent.Attack 상태일 때만 공격 처리
            if (intentState.State != Intent.Attack)
            {
                if (actionState.State == Action.Attacking)
                {
                    actionState.State = Action.Idle;
                }
                return;
            }

            Entity targetEntity = intentState.TargetEntity;

            // AggroTarget 동기화
            if (aggroTarget.TargetEntity != targetEntity)
            {
                aggroTarget.TargetEntity = targetEntity;
                if (TransformLookup.TryGetComponent(targetEntity, out LocalTransform targetTransformForSync))
                {
                    aggroTarget.LastTargetPosition = targetTransformForSync.Position;
                }
            }

            // 타겟 없음
            if (targetEntity == Entity.Null)
            {
                intentState.State = Intent.Idle;
                actionState.State = Action.Idle;
                return;
            }

            // 타겟 유효성 + 사망 체크
            if (!CombatUtility.IsTargetAlive(targetEntity, in TransformLookup, in HealthLookup, out var targetTransform))
            {
                intentState.State = Intent.Idle;
                intentState.TargetEntity = Entity.Null;
                aggroTarget.TargetEntity = Entity.Null;
                actionState.State = Action.Idle;
                return;
            }

            float3 targetPos = targetTransform.Position;
            float3 myPos = transform.Position;
            float effectiveDist = CombatUtility.GetEffectiveDistance(myPos, targetPos, targetEntity, in ObstacleRadiusLookup);

            bool isInRange = effectiveDist <= combatStats.AttackRange;

            if (isInRange)
            {
                actionState.State = Action.Attacking;
                CombatUtility.RotateTowardTarget(in myPos, in targetPos, ref transform.Rotation);

                if (cooldown.RemainingTime <= 0)
                {
                    CombatUtility.ApplyDamage(ref ECB, sortKey, targetEntity, entity, combatStats.AttackPower, in DefenseLookup);
                    cooldown.RemainingTime = CombatUtility.ResetCooldown(combatStats.AttackSpeed);
                }
            }
            else
            {
                // 사거리 밖 → 이동 중
                actionState.State = Action.Moving;
            }
        }
    }
}
