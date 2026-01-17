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
    /// - 거리 기반 히트 판정 (RTS 스타일)
    /// - IJobEntity + Burst로 최적화
    /// - ECB.ParallelWriter를 통한 스레드 안전 데미지 이벤트 추가
    /// - distancesq로 sqrt 연산 제거
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

            // =====================================================================
            // 1. 적 유닛 근접 공격 Job
            // =====================================================================
            var enemyJob = new EnemyMeleeAttackJob
            {
                DeltaTime = deltaTime,
                ECB = ecb,
                TransformLookup = transformLookup,
                HealthLookup = healthLookup,
                DefenseLookup = defenseLookup
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
                DefenseLookup = defenseLookup
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

        private void Execute(
            Entity entity,
            [ChunkIndexInQuery] int sortKey,
            ref LocalTransform transform,
            in AggroTarget aggroTarget,
            in CombatStats combatStats,
            ref AttackCooldown cooldown,
            ref EnemyState enemyState)
        {
            cooldown.RemainingTime = math.max(0, cooldown.RemainingTime - DeltaTime);

            Entity targetEntity = aggroTarget.TargetEntity;
            if (targetEntity == Entity.Null) return;

            // TryGetComponent로 존재 여부 + 값 획득 동시 처리
            if (!HealthLookup.TryGetComponent(targetEntity, out Health targetHealth) ||
                !TransformLookup.TryGetComponent(targetEntity, out LocalTransform targetTransform))
            {
                return;
            }

            if (targetHealth.CurrentValue <= 0) return;

            // [최적화] 거리 제곱 사용 (sqrt 제거)
            float3 targetPos = targetTransform.Position;
            float3 myPos = transform.Position;
            float distSq = math.distancesq(myPos, targetPos);
            float rangeSq = combatStats.AttackRange * combatStats.AttackRange;

            bool isInRange = distSq <= rangeSq;

            if (isInRange)
            {
                // 공격 범위 내 (이동 정지)
                if (enemyState.CurrentState != EnemyContext.Attacking)
                {
                    enemyState.CurrentState = EnemyContext.Attacking;
                    ECB.SetComponentEnabled<MovementWaypoints>(sortKey, entity, false);
                }

                // 타겟 방향 회전
                float3 direction = targetPos - myPos;
                direction.y = 0;

                if (math.lengthsq(direction) > 0.001f)
                {
                    transform.Rotation = quaternion.LookRotationSafe(math.normalize(direction), math.up());
                }

                // 쿨다운 체크 후 공격
                if (cooldown.RemainingTime <= 0)
                {
                    float defenseValue = DefenseLookup.TryGetComponent(targetEntity, out Defense defense)
                        ? defense.Value
                        : 0f;

                    float finalDamage = DamageUtility.CalculateDamage(combatStats.AttackPower, defenseValue);
                    ECB.AppendToBuffer(sortKey, targetEntity, new DamageEvent { Damage = finalDamage });

                    // 쿨다운 리셋
                    cooldown.RemainingTime = combatStats.AttackSpeed > 0
                        ? 1.0f / combatStats.AttackSpeed
                        : 1.0f;
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

        private void Execute(
            [ChunkIndexInQuery] int sortKey,
            ref LocalTransform transform,
            ref AggroTarget aggroTarget,
            in CombatStats combatStats,
            ref AttackCooldown cooldown,
            ref UnitIntentState intentState,
            ref UnitActionState actionState)
        {
            cooldown.RemainingTime = math.max(0, cooldown.RemainingTime - DeltaTime);

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

            // TryGetComponent로 존재 여부 + 값 획득 동시 처리
            if (!HealthLookup.TryGetComponent(targetEntity, out Health targetHealth) ||
                !TransformLookup.TryGetComponent(targetEntity, out LocalTransform targetTransform))
            {
                intentState.State = Intent.Idle;
                intentState.TargetEntity = Entity.Null;
                aggroTarget.TargetEntity = Entity.Null;
                actionState.State = Action.Idle;
                return;
            }

            // 타겟 사망 체크
            if (targetHealth.CurrentValue <= 0)
            {
                intentState.State = Intent.Idle;
                intentState.TargetEntity = Entity.Null;
                aggroTarget.TargetEntity = Entity.Null;
                actionState.State = Action.Idle;
                return;
            }

            // [최적화] 거리 제곱 사용 (sqrt 제거)
            float3 targetPos = targetTransform.Position;
            float3 myPos = transform.Position;
            float distSq = math.distancesq(myPos, targetPos);
            float rangeSq = combatStats.AttackRange * combatStats.AttackRange;

            bool isInRange = distSq <= rangeSq;

            if (isInRange)
            {
                actionState.State = Action.Attacking;

                // 타겟 방향 회전
                float3 direction = targetPos - myPos;
                direction.y = 0;

                if (math.lengthsq(direction) > 0.001f)
                {
                    transform.Rotation = quaternion.LookRotationSafe(math.normalize(direction), math.up());
                }

                // 쿨다운 체크 후 공격
                if (cooldown.RemainingTime <= 0)
                {
                    float defenseValue = DefenseLookup.TryGetComponent(targetEntity, out Defense defense)
                        ? defense.Value
                        : 0f;

                    float finalDamage = DamageUtility.CalculateDamage(combatStats.AttackPower, defenseValue);
                    ECB.AppendToBuffer(sortKey, targetEntity, new DamageEvent { Damage = finalDamage });

                    // 쿨다운 리셋
                    cooldown.RemainingTime = combatStats.AttackSpeed > 0
                        ? 1.0f / combatStats.AttackSpeed
                        : 1.0f;
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
