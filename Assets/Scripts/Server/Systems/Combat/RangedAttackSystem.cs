using Shared;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Server
{
    /// <summary>
    /// 원거리 공격 시스템
    /// - RangedUnitTag를 가진 유닛 처리 (Trooper, Sniper 등)
    /// - RangedEnemyTag를 가진 적 처리 (EnemyFlying 등)
    /// - AggroTarget 기반 자동 공격
    /// - 사거리 내: 멈추고 공격, 사거리 밖: 이동
    /// - 거리 판정: 직선거리 - 타겟 반지름(ObstacleRadius)으로 유효 거리 계산
    /// - 거리 판정 후 즉시 데미지 적용 (필중)
    /// - 공격 전 타겟 방향 회전
    /// - 서버에서 시각 효과 투사체 생성 (Ghost로 클라이언트에 복제)
    /// - IJobEntity + Burst + ECB.ParallelWriter로 병렬 처리
    /// </summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
    [UpdateAfter(typeof(MeleeAttackSystem))]
    public partial struct RangedAttackSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
            state.RequireForUpdate<ProjectilePrefabRef>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float deltaTime = SystemAPI.Time.DeltaTime;

            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();

            var projectilePrefab = SystemAPI.GetSingleton<ProjectilePrefabRef>().Prefab;

            // 공통 ReadOnly Lookup
            var transformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true);
            var healthLookup = SystemAPI.GetComponentLookup<Health>(true);
            var defenseLookup = SystemAPI.GetComponentLookup<Defense>(true);
            var teamLookup = SystemAPI.GetComponentLookup<Team>(true);
            var obstacleRadiusLookup = SystemAPI.GetComponentLookup<ObstacleRadius>(true);

            // =====================================================================
            // 1. 원거리 유닛 공격 Job (RangedUnitTag)
            // =====================================================================
            var unitJob = new RangedUnitAttackJob
            {
                DeltaTime = deltaTime,
                ECB = ecb,
                ProjectilePrefab = projectilePrefab,
                TransformLookup = transformLookup,
                HealthLookup = healthLookup,
                DefenseLookup = defenseLookup,
                TeamLookup = teamLookup,
                ObstacleRadiusLookup = obstacleRadiusLookup
            };
            state.Dependency = unitJob.ScheduleParallel(state.Dependency);

            // =====================================================================
            // 2. 원거리 적 공격 Job (RangedEnemyTag)
            // =====================================================================
            var enemyJob = new RangedEnemyAttackJob
            {
                DeltaTime = deltaTime,
                ECB = ecb,
                ProjectilePrefab = projectilePrefab,
                TransformLookup = transformLookup,
                HealthLookup = healthLookup,
                DefenseLookup = defenseLookup,
                TeamLookup = teamLookup,
                ObstacleRadiusLookup = obstacleRadiusLookup
            };
            state.Dependency = enemyJob.ScheduleParallel(state.Dependency);
        }
    }

    /// <summary>
    /// 원거리 유닛 공격 Job (Trooper, Sniper 등)
    /// </summary>
    [BurstCompile]
    [WithAll(typeof(UnitTag), typeof(RangedUnitTag))]
    public partial struct RangedUnitAttackJob : IJobEntity
    {
        public float DeltaTime;
        public EntityCommandBuffer.ParallelWriter ECB;
        public Entity ProjectilePrefab;

        [ReadOnly, NativeDisableContainerSafetyRestriction]
        public ComponentLookup<LocalTransform> TransformLookup;
        [ReadOnly] public ComponentLookup<Health> HealthLookup;
        [ReadOnly] public ComponentLookup<Defense> DefenseLookup;
        [ReadOnly] public ComponentLookup<Team> TeamLookup;
        [ReadOnly] public ComponentLookup<ObstacleRadius> ObstacleRadiusLookup;

        private void Execute(
            Entity entity,
            [ChunkIndexInQuery] int sortKey,
            ref LocalTransform transform,
            ref AggroTarget aggroTarget,
            in CombatStats combatStats,
            ref AttackCooldown cooldown,
            ref UnitIntentState intentState,
            ref UnitActionState actionState,
            in Team myTeam,
            ref MovementGoal movementGoal)
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

            // AggroTarget 동기화
            Entity targetEntity = intentState.TargetEntity;
            if (aggroTarget.TargetEntity != targetEntity)
            {
                aggroTarget.TargetEntity = targetEntity;
                if (TransformLookup.TryGetComponent(targetEntity, out var syncTransform))
                {
                    aggroTarget.LastTargetPosition = syncTransform.Position;
                }
            }

            // 타겟 유효성 검사
            if (targetEntity == Entity.Null)
            {
                intentState.State = Intent.Idle;
                actionState.State = Action.Idle;
                return;
            }

            if (!CombatUtility.IsTargetAlive(targetEntity, in TransformLookup, in HealthLookup, out var targetTransform))
            {
                intentState.State = Intent.Idle;
                intentState.TargetEntity = Entity.Null;
                aggroTarget.TargetEntity = Entity.Null;
                actionState.State = Action.Idle;
                return;
            }

            // 아군 히트 방지
            if (TeamLookup.TryGetComponent(targetEntity, out var targetTeam))
            {
                if (targetTeam.teamId == myTeam.teamId)
                {
                    intentState.State = Intent.Idle;
                    intentState.TargetEntity = Entity.Null;
                    aggroTarget.TargetEntity = Entity.Null;
                    actionState.State = Action.Idle;
                    return;
                }
            }

            float3 targetPos = targetTransform.Position;
            float3 myPos = transform.Position;
            float effectiveDistance = CombatUtility.GetEffectiveDistance(myPos, targetPos, targetEntity, in ObstacleRadiusLookup);

            if (effectiveDistance > combatStats.AttackRange)
            {
                // 사거리 밖 -> 타겟 위치로 이동 요청
                actionState.State = Action.Moving;

                if (math.distancesq(movementGoal.Destination, targetPos) > 1.0f)
                {
                    movementGoal.Destination = targetPos;
                    movementGoal.IsPathDirty = true;
                    movementGoal.CurrentWaypointIndex = 0;
                }
                return;
            }

            // 사거리 내 -> 이동 비활성화
            ECB.SetComponentEnabled<MovementWaypoints>(sortKey, entity, false);
            CombatUtility.RotateTowardTarget(in myPos, in targetPos, ref transform.Rotation);

            actionState.State = Action.Attacking;

            if (cooldown.RemainingTime > 0) return;

            CombatUtility.ApplyDamage(ref ECB, sortKey, targetEntity, entity, combatStats.AttackPower, in DefenseLookup);
            CombatUtility.SpawnVisualProjectile(ref ECB, sortKey, ProjectilePrefab, in myPos, in targetPos);
            cooldown.RemainingTime = CombatUtility.ResetCooldown(combatStats.AttackSpeed);
        }
    }

    /// <summary>
    /// 원거리 적 공격 Job (EnemyFlying 등)
    /// </summary>
    [BurstCompile]
    [WithAll(typeof(EnemyTag), typeof(RangedEnemyTag))]
    public partial struct RangedEnemyAttackJob : IJobEntity
    {
        public float DeltaTime;
        public EntityCommandBuffer.ParallelWriter ECB;
        public Entity ProjectilePrefab;

        [ReadOnly, NativeDisableContainerSafetyRestriction]
        public ComponentLookup<LocalTransform> TransformLookup;
        [ReadOnly] public ComponentLookup<Health> HealthLookup;
        [ReadOnly] public ComponentLookup<Defense> DefenseLookup;
        [ReadOnly] public ComponentLookup<Team> TeamLookup;
        [ReadOnly] public ComponentLookup<ObstacleRadius> ObstacleRadiusLookup;

        private void Execute(
            Entity entity,
            [ChunkIndexInQuery] int sortKey,
            ref LocalTransform transform,
            in AggroTarget aggroTarget,
            in CombatStats combatStats,
            ref AttackCooldown cooldown,
            ref EnemyState enemyState,
            in Team myTeam)
        {
            cooldown.RemainingTime = CombatUtility.TickCooldown(cooldown.RemainingTime, DeltaTime);

            Entity targetEntity = aggroTarget.TargetEntity;
            if (targetEntity == Entity.Null) return;

            if (!CombatUtility.IsTargetAlive(targetEntity, in TransformLookup, in HealthLookup, out var targetTransform))
                return;

            // 아군 히트 방지
            if (TeamLookup.TryGetComponent(targetEntity, out var targetTeam))
            {
                if (targetTeam.teamId == myTeam.teamId)
                    return;
            }

            float3 targetPos = targetTransform.Position;
            float3 myPos = transform.Position;
            float effectiveDistance = CombatUtility.GetEffectiveDistance(myPos, targetPos, targetEntity, in ObstacleRadiusLookup);

            if (effectiveDistance > combatStats.AttackRange)
            {
                // 사거리 밖 -> 추적
                if (enemyState.CurrentState == EnemyContext.Attacking)
                {
                    enemyState.CurrentState = EnemyContext.Chasing;
                    ECB.SetComponentEnabled<MovementWaypoints>(sortKey, entity, true);

                    ECB.SetComponent(sortKey, entity, new MovementGoal
                    {
                        Destination = targetPos,
                        IsPathDirty = true,
                        CurrentWaypointIndex = 0
                    });
                }
                return;
            }

            // 사거리 내 -> 멈추고 공격
            if (enemyState.CurrentState != EnemyContext.Attacking)
            {
                enemyState.CurrentState = EnemyContext.Attacking;
                ECB.SetComponentEnabled<MovementWaypoints>(sortKey, entity, false);
            }

            CombatUtility.RotateTowardTarget(in myPos, in targetPos, ref transform.Rotation);

            if (cooldown.RemainingTime > 0) return;

            CombatUtility.ApplyDamage(ref ECB, sortKey, targetEntity, entity, combatStats.AttackPower, in DefenseLookup);
            CombatUtility.SpawnVisualProjectile(ref ECB, sortKey, ProjectilePrefab, in myPos, in targetPos);
            cooldown.RemainingTime = CombatUtility.ResetCooldown(combatStats.AttackSpeed);
        }
    }
}
