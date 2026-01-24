using Shared;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

namespace Server
{
    /// <summary>
    /// 원거리 공격 시스템
    /// - RangedUnitTag를 가진 유닛 처리 (Trooper, Sniper 등)
    /// - AggroTarget 기반 자동 공격
    /// - 사거리 내: 멈추고 공격, 사거리 밖: 이동
    /// - 거리 판정 후 즉시 데미지 적용 (필중)
    /// - 공격 전 타겟 방향 회전
    /// - 서버에서 시각 효과 투사체 생성 (Ghost로 클라이언트에 복제)
    /// </summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
    [UpdateAfter(typeof(MeleeAttackSystem))]
    public partial struct RangedAttackSystem : ISystem
    {
        private ComponentLookup<LocalTransform> _transformLookup;
        private ComponentLookup<Health> _healthLookup;
        private ComponentLookup<Defense> _defenseLookup;
        private ComponentLookup<Team> _teamLookup;
        private ComponentLookup<MovementGoal> _movementGoalLookup;
        private BufferLookup<DamageEvent> _damageEventLookup;

        private EntityQuery _projectilePrefabQuery;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
            state.RequireForUpdate<ProjectilePrefabRef>();

            _transformLookup = state.GetComponentLookup<LocalTransform>(true);
            _healthLookup = state.GetComponentLookup<Health>(true);
            _defenseLookup = state.GetComponentLookup<Defense>(true);
            _teamLookup = state.GetComponentLookup<Team>(true);
            _movementGoalLookup = state.GetComponentLookup<MovementGoal>(false);
            _damageEventLookup = state.GetBufferLookup<DamageEvent>(false);

            _projectilePrefabQuery = state.GetEntityQuery(ComponentType.ReadOnly<ProjectilePrefabRef>());
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float deltaTime = SystemAPI.Time.DeltaTime;

            // Lookup 업데이트
            _transformLookup.Update(ref state);
            _healthLookup.Update(ref state);
            _defenseLookup.Update(ref state);
            _teamLookup.Update(ref state);
            _movementGoalLookup.Update(ref state);
            _damageEventLookup.Update(ref state);

            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

            // 투사체 프리팹 가져오기
            var prefabRef = _projectilePrefabQuery.GetSingleton<ProjectilePrefabRef>();
            Entity projectilePrefab = prefabRef.Prefab;

            // =====================================================================
            // 원거리 유닛 공격 처리 (RangedUnitTag로 통합 쿼리)
            // - Trooper, Sniper 등 모든 원거리 유닛을 단일 쿼리로 처리
            // =====================================================================
            foreach (var (transform, aggroTarget, combatStats, cooldown, intentState, actionState, myTeam, entity) in
                     SystemAPI.Query<
                         RefRW<LocalTransform>,
                         RefRW<AggroTarget>,
                         RefRO<CombatStats>,
                         RefRW<AttackCooldown>,
                         RefRW<UnitIntentState>,
                         RefRW<UnitActionState>,
                         RefRO<Team>>()
                     .WithAll<UnitTag, RangedUnitTag>()
                     .WithEntityAccess())
            {
                ProcessRangedAttack(
                    entity,
                    ref transform.ValueRW,
                    ref aggroTarget.ValueRW,
                    combatStats.ValueRO,
                    ref cooldown.ValueRW,
                    ref intentState.ValueRW,
                    ref actionState.ValueRW,
                    myTeam.ValueRO,
                    deltaTime,
                    ecb,
                    projectilePrefab,
                    ref _movementGoalLookup);
            }

            // =====================================================================
            // 원거리 적 공격 처리 (RangedEnemyTag)
            // - EnemyFlying 등 원거리 공격 적 처리
            // =====================================================================
            foreach (var (transform, aggroTarget, combatStats, cooldown, enemyState, myTeam, entity) in
                     SystemAPI.Query<
                         RefRW<LocalTransform>,
                         RefRO<AggroTarget>,
                         RefRO<CombatStats>,
                         RefRW<AttackCooldown>,
                         RefRW<EnemyState>,
                         RefRO<Team>>()
                     .WithAll<EnemyTag, RangedEnemyTag>()
                     .WithEntityAccess())
            {
                ProcessEnemyRangedAttack(
                    entity,
                    ref transform.ValueRW,
                    aggroTarget.ValueRO,
                    combatStats.ValueRO,
                    ref cooldown.ValueRW,
                    ref enemyState.ValueRW,
                    myTeam.ValueRO,
                    deltaTime,
                    ecb,
                    projectilePrefab,
                    ref _movementGoalLookup);
            }
        }

        /// <summary>
        /// 원거리 공격 처리 (공통 로직)
        /// - 사거리 밖: 이동 활성화
        /// - 사거리 내: 이동 비활성화, 멈추고 공격
        /// </summary>
        private void ProcessRangedAttack(
            Entity unitEntity,
            ref LocalTransform myTransform,
            ref AggroTarget aggroTarget,
            CombatStats stats,
            ref AttackCooldown cooldown,
            ref UnitIntentState intentState,
            ref UnitActionState actionState,
            Team myTeam,
            float deltaTime,
            EntityCommandBuffer ecb,
            Entity projectilePrefab,
            ref ComponentLookup<MovementGoal> movementGoalLookup)
        {
            // 쿨다운 감소
            cooldown.RemainingTime = math.max(0, cooldown.RemainingTime - deltaTime);

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
                if (_transformLookup.TryGetComponent(targetEntity, out var syncTransform))
                {
                    aggroTarget.LastTargetPosition = syncTransform.Position;
                }
            }

            // 타겟 유효성 검사 (TryGetComponent로 중복 룩업 제거)
            if (targetEntity == Entity.Null)
            {
                intentState.State = Intent.Idle;
                actionState.State = Action.Idle;
                return;
            }

            if (!_transformLookup.TryGetComponent(targetEntity, out var targetTransform) ||
                !_healthLookup.TryGetComponent(targetEntity, out var targetHealth))
            {
                intentState.State = Intent.Idle;
                intentState.TargetEntity = Entity.Null;
                aggroTarget.TargetEntity = Entity.Null;
                actionState.State = Action.Idle;
                return;
            }

            // 아군 히트 방지
            if (_teamLookup.TryGetComponent(targetEntity, out var targetTeam))
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

            // 타겟이 이미 사망했으면 무시
            if (targetHealth.CurrentValue <= 0)
            {
                intentState.State = Intent.Idle;
                intentState.TargetEntity = Entity.Null;
                aggroTarget.TargetEntity = Entity.Null;
                actionState.State = Action.Idle;
                return;
            }

            // 거리 계산 (위에서 TryGetComponent로 이미 조회한 targetTransform 사용)
            float3 targetPos = targetTransform.Position;
            float3 myPos = myTransform.Position;
            float distance = math.distance(myPos, targetPos);

            // 공격 사거리 체크
            if (distance > stats.AttackRange)
            {
                // 사거리 밖 → 타겟 위치로 이동 요청
                actionState.State = Action.Moving;

                // MovementGoal 업데이트 (타겟 위치로 새 경로 계산 트리거)
                // PathfindingSystem이 경로를 찾으면 MovementWaypoints를 활성화함
                if (movementGoalLookup.HasComponent(unitEntity))
                {
                    var goal = movementGoalLookup[unitEntity];
                    // 목적지가 변경되었을 때만 새 경로 계산 (떨림 방지)
                    if (math.distancesq(goal.Destination, targetPos) > 1.0f)
                    {
                        goal.Destination = targetPos;
                        goal.IsPathDirty = true;
                        goal.CurrentWaypointIndex = 0;
                        movementGoalLookup[unitEntity] = goal;
                    }
                }
                return;
            }

            // 사거리 내 → 이동 비활성화 (멈춤)
            ecb.SetComponentEnabled<MovementWaypoints>(unitEntity, false);

            // 타겟 방향 회전
            float3 direction = math.normalize(targetPos - myPos);
            direction.y = 0; // Y축 회전만 (수평 회전)
            if (math.lengthsq(direction) > 0.001f)
            {
                myTransform.Rotation = quaternion.LookRotationSafe(direction, math.up());
            }

            actionState.State = Action.Attacking;

            // 쿨다운 체크
            if (cooldown.RemainingTime > 0) return;

            // 데미지 계산 (TryGetComponent로 중복 룩업 제거)
            float defenseValue = _defenseLookup.TryGetComponent(targetEntity, out var defense)
                ? defense.Value
                : 0f;

            float finalDamage = DamageUtility.CalculateDamage(stats.AttackPower, defenseValue);

            // DamageEvent 버퍼에 데미지 추가 (즉시 적용 = 필중)
            if (_damageEventLookup.TryGetBuffer(targetEntity, out var damageBuffer))
            {
                damageBuffer.Add(new DamageEvent { Damage = finalDamage, Attacker = unitEntity });
            }

            // 시각 효과 투사체 생성 (서버에서 생성, Ghost로 클라이언트에 복제됨)
            float3 startPos = myPos + new float3(0, 1f, 0);
            float3 endPos = targetPos + new float3(0, 1f, 0);
            float3 projectileDir = math.normalize(endPos - startPos);
            float projectileDistance = math.distance(startPos, endPos);

            Entity projectile = ecb.Instantiate(projectilePrefab);

            // 위치 및 회전 설정
            quaternion rotation = quaternion.LookRotationSafe(projectileDir, math.up());
            ecb.SetComponent(projectile, LocalTransform.FromPositionRotationScale(startPos, rotation, 1f));

            // 이동 데이터 설정
            ecb.SetComponent(projectile, new ProjectileMove
            {
                Direction = projectileDir,
                Speed = 30f,
                RemainingDistance = projectileDistance
            });

            // 시각 전용 태그 추가 (CombatDamageSystem에서 무시하도록)
            ecb.AddComponent(projectile, new VisualOnlyTag());

            // 쿨다운 리셋
            cooldown.RemainingTime = stats.AttackSpeed > 0
                ? 1.0f / stats.AttackSpeed
                : 1.0f;
        }

        /// <summary>
        /// 적 원거리 공격 처리
        /// - AggroTarget 기반 자동 공격
        /// - 사거리 내: 멈추고 공격, 사거리 밖: 추적
        /// </summary>
        private void ProcessEnemyRangedAttack(
            Entity enemyEntity,
            ref LocalTransform myTransform,
            AggroTarget aggroTarget,
            CombatStats stats,
            ref AttackCooldown cooldown,
            ref EnemyState enemyState,
            Team myTeam,
            float deltaTime,
            EntityCommandBuffer ecb,
            Entity projectilePrefab,
            ref ComponentLookup<MovementGoal> movementGoalLookup)
        {
            // 쿨다운 감소
            cooldown.RemainingTime = math.max(0, cooldown.RemainingTime - deltaTime);

            Entity targetEntity = aggroTarget.TargetEntity;

            // 타겟 없음
            if (targetEntity == Entity.Null) return;

            // 타겟 유효성 검사 (TryGetComponent로 중복 룩업 제거)
            if (!_transformLookup.TryGetComponent(targetEntity, out var targetTransform) ||
                !_healthLookup.TryGetComponent(targetEntity, out var targetHealth))
            {
                return;
            }

            // 아군 히트 방지
            if (_teamLookup.TryGetComponent(targetEntity, out var targetTeam))
            {
                if (targetTeam.teamId == myTeam.teamId)
                    return;
            }

            // 타겟이 이미 사망했으면 무시
            if (targetHealth.CurrentValue <= 0) return;

            // 거리 계산 (위에서 TryGetComponent로 이미 조회한 targetTransform 사용)
            float3 targetPos = targetTransform.Position;
            float3 myPos = myTransform.Position;
            float distance = math.distance(myPos, targetPos);

            // 공격 사거리 체크
            if (distance > stats.AttackRange)
            {
                // 사거리 밖 → 추적
                if (enemyState.CurrentState == EnemyContext.Attacking)
                {
                    enemyState.CurrentState = EnemyContext.Chasing;
                    ecb.SetComponentEnabled<MovementWaypoints>(enemyEntity, true);

                    // 경로 재계산 트리거
                    if (movementGoalLookup.TryGetComponent(enemyEntity, out var goal))
                    {
                        goal.IsPathDirty = true;
                        movementGoalLookup[enemyEntity] = goal;
                    }
                }
                return;
            }

            // 사거리 내 → 멈추고 공격
            if (enemyState.CurrentState != EnemyContext.Attacking)
            {
                enemyState.CurrentState = EnemyContext.Attacking;
                ecb.SetComponentEnabled<MovementWaypoints>(enemyEntity, false);
            }

            // 타겟 방향 회전
            float3 direction = math.normalize(targetPos - myPos);
            direction.y = 0;
            if (math.lengthsq(direction) > 0.001f)
            {
                myTransform.Rotation = quaternion.LookRotationSafe(direction, math.up());
            }

            // 쿨다운 체크
            if (cooldown.RemainingTime > 0) return;

            // 데미지 계산 (TryGetComponent로 중복 룩업 제거)
            float defenseValue = _defenseLookup.TryGetComponent(targetEntity, out var defense)
                ? defense.Value
                : 0f;

            float finalDamage = DamageUtility.CalculateDamage(stats.AttackPower, defenseValue);

            // DamageEvent 버퍼에 데미지 추가 (즉시 적용 = 필중)
            if (_damageEventLookup.TryGetBuffer(targetEntity, out var damageBuffer))
            {
                damageBuffer.Add(new DamageEvent { Damage = finalDamage, Attacker = enemyEntity });
            }

            // 시각 효과 투사체 생성
            float3 startPos = myPos + new float3(0, 1f, 0);
            float3 endPos = targetPos + new float3(0, 1f, 0);
            float3 projectileDir = math.normalize(endPos - startPos);
            float projectileDistance = math.distance(startPos, endPos);

            Entity projectile = ecb.Instantiate(projectilePrefab);

            quaternion rotation = quaternion.LookRotationSafe(projectileDir, math.up());
            ecb.SetComponent(projectile, LocalTransform.FromPositionRotationScale(startPos, rotation, 1f));

            ecb.SetComponent(projectile, new ProjectileMove
            {
                Direction = projectileDir,
                Speed = 30f,
                RemainingDistance = projectileDistance
            });

            ecb.AddComponent(projectile, new VisualOnlyTag());

            // 쿨다운 리셋
            cooldown.RemainingTime = stats.AttackSpeed > 0
                ? 1.0f / stats.AttackSpeed
                : 1.0f;
        }
    }
}
