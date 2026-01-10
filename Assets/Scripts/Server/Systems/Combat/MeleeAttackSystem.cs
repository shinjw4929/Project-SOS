using Shared;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Server
{
    /// <summary>
    /// 근접 공격 시스템
    /// - 유닛과 적 모두 AggroTarget 기반으로 동일하게 처리
    /// - 거리 기반 히트 판정 (RTS 스타일)
    /// - ECB를 통한 DamageEvent 버퍼 추가 (Job 스케줄링 충돌 방지)
    /// </summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
    [UpdateAfter(typeof(CombatDamageSystem))]
    public partial struct MeleeAttackSystem : ISystem
    {
        // 타겟 정보 조회용 Lookup (모두 읽기 전용)
        private ComponentLookup<LocalTransform> _transformLookup;
        private ComponentLookup<Health> _healthLookup;
        private ComponentLookup<Defense> _defenseLookup;
        private ComponentLookup<Team> _teamLookup;
        private BufferLookup<DamageEvent> _damageEventLookup;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();

            _transformLookup = state.GetComponentLookup<LocalTransform>(true);
            _healthLookup = state.GetComponentLookup<Health>(true); // 읽기 전용으로 변경
            _defenseLookup = state.GetComponentLookup<Defense>(true);
            _teamLookup = state.GetComponentLookup<Team>(true);
            _damageEventLookup = state.GetBufferLookup<DamageEvent>(false); // 쓰기 필요
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // CombatDamageSystem의 Job이 DamageEvent 버퍼를 읽고 있으므로
            // 쓰기 전에 완료를 기다림
            state.CompleteDependency();

            float deltaTime = SystemAPI.Time.DeltaTime;

            // Lookup 업데이트
            _transformLookup.Update(ref state);
            _healthLookup.Update(ref state);
            _defenseLookup.Update(ref state);
            _teamLookup.Update(ref state);
            _damageEventLookup.Update(ref state);

            // =====================================================================
            // 1. 적 유닛 근접 공격 처리
            // =====================================================================
            foreach (var (transform, aggroTarget, combatStats, cooldown, enemyState, myTeam) in
                     SystemAPI.Query<
                         RefRW<LocalTransform>,
                         RefRO<AggroTarget>,
                         RefRO<CombatStats>,
                         RefRW<AttackCooldown>,
                         RefRW<EnemyState>,
                         RefRO<Team>>()
                     .WithAll<EnemyTag>())
            {
                ProcessMeleeAttack(
                    ref transform.ValueRW,
                    aggroTarget.ValueRO.TargetEntity,
                    combatStats.ValueRO,
                    ref cooldown.ValueRW,
                    myTeam.ValueRO,
                    deltaTime,
                    out bool isInRange);

                // 상태 전환: 사거리 내면 Attacking, 아니면 Chasing 유지
                if (aggroTarget.ValueRO.TargetEntity != Entity.Null)
                {
                    if (isInRange)
                    {
                        enemyState.ValueRW.CurrentState = EnemyContext.Attacking;
                    }
                    else if (enemyState.ValueRO.CurrentState == EnemyContext.Attacking)
                    {
                        // 사거리 밖으로 나가면 다시 추격
                        enemyState.ValueRW.CurrentState = EnemyContext.Chasing;
                    }
                }
            }

            // =====================================================================
            // 2. 아군 유닛 근접 공격 처리 (원거리 유닛 제외)
            // =====================================================================
            foreach (var (transform, aggroTarget, combatStats, cooldown, intentState, actionState, myTeam) in
                     SystemAPI.Query<
                         RefRW<LocalTransform>,
                         RefRW<AggroTarget>,
                         RefRO<CombatStats>,
                         RefRW<AttackCooldown>,
                         RefRW<UnitIntentState>,
                         RefRW<UnitActionState>,
                         RefRO<Team>>()
                     .WithAll<UnitTag>()
                     .WithNone<RangedUnitTag>())
            {
                // Intent.Attack 상태일 때만 공격 처리
                if (intentState.ValueRO.State != Intent.Attack)
                {
                    // 공격 상태가 아니면 Attacking 액션 해제
                    if (actionState.ValueRO.State == Action.Attacking)
                    {
                        actionState.ValueRW.State = Action.Idle;
                    }
                    continue;
                }

                // AggroTarget 동기화 (UnitIntentState.TargetEntity와 일치)
                Entity targetEntity = intentState.ValueRO.TargetEntity;
                if (aggroTarget.ValueRO.TargetEntity != targetEntity)
                {
                    aggroTarget.ValueRW.TargetEntity = targetEntity;
                    if (_transformLookup.TryGetComponent(targetEntity, out var targetTransform))
                    {
                        aggroTarget.ValueRW.LastTargetPosition = targetTransform.Position;
                    }
                }

                ProcessMeleeAttack(
                    ref transform.ValueRW,
                    targetEntity,
                    combatStats.ValueRO,
                    ref cooldown.ValueRW,
                    myTeam.ValueRO,
                    deltaTime,
                    out bool isInRange);

                // 상태 전환
                if (targetEntity != Entity.Null)
                {
                    // 타겟이 사망했는지 확인
                    if (!_healthLookup.HasComponent(targetEntity) ||
                        _healthLookup[targetEntity].CurrentValue <= 0)
                    {
                        // 타겟 사망 → Idle로 전환
                        intentState.ValueRW.State = Intent.Idle;
                        intentState.ValueRW.TargetEntity = Entity.Null;
                        aggroTarget.ValueRW.TargetEntity = Entity.Null;
                        actionState.ValueRW.State = Action.Idle;
                    }
                    else if (isInRange)
                    {
                        actionState.ValueRW.State = Action.Attacking;
                    }
                    else
                    {
                        // 사거리 밖 → 이동 중
                        actionState.ValueRW.State = Action.Moving;
                    }
                }
                else
                {
                    // 타겟 없음 → Idle
                    intentState.ValueRW.State = Intent.Idle;
                    actionState.ValueRW.State = Action.Idle;
                }
            }
        }

        /// <summary>
        /// 근접 공격 처리 (공통 로직)
        /// - 타겟 방향 회전
        /// - 데미지를 DamageEvent 버퍼에 추가 (ECB 패턴)
        /// </summary>
        private void ProcessMeleeAttack(
            ref LocalTransform myTransform,
            Entity targetEntity,
            CombatStats stats,
            ref AttackCooldown cooldown,
            Team myTeam,
            float deltaTime,
            out bool isInRange)
        {
            isInRange = false;

            // 쿨다운 감소
            cooldown.RemainingTime = math.max(0, cooldown.RemainingTime - deltaTime);

            // 타겟 유효성 검사
            if (targetEntity == Entity.Null) return;
            if (!_transformLookup.HasComponent(targetEntity)) return;
            if (!_healthLookup.HasComponent(targetEntity)) return;

            // 아군 히트 방지
            if (_teamLookup.HasComponent(targetEntity))
            {
                if (_teamLookup[targetEntity].teamId == myTeam.teamId)
                    return;
            }

            // 타겟이 이미 사망했으면 무시
            var targetHealth = _healthLookup[targetEntity];
            if (targetHealth.CurrentValue <= 0) return;

            // 거리 계산
            float3 targetPos = _transformLookup[targetEntity].Position;
            float3 myPos = myTransform.Position;
            float distance = math.distance(myPos, targetPos);

            // 공격 사거리 체크
            if (distance > stats.AttackRange)
            {
                isInRange = false;
                return;
            }

            isInRange = true;

            // 타겟 방향 회전 (Y축만)
            float3 direction = math.normalize(targetPos - myPos);
            direction.y = 0;
            if (math.lengthsq(direction) > 0.001f)
            {
                myTransform.Rotation = quaternion.LookRotationSafe(direction, math.up());
            }

            // 쿨다운 체크
            if (cooldown.RemainingTime > 0) return;

            // 데미지 계산
            float defenseValue = _defenseLookup.HasComponent(targetEntity)
                ? _defenseLookup[targetEntity].Value
                : 0f;

            float finalDamage = DamageUtility.CalculateDamage(stats.AttackPower, defenseValue);

            // DamageEvent 버퍼에 데미지 추가 (ECB 대신 직접 버퍼 접근)
            if (_damageEventLookup.HasBuffer(targetEntity))
            {
                var damageBuffer = _damageEventLookup[targetEntity];
                damageBuffer.Add(new DamageEvent { Damage = finalDamage });
            }

            // 쿨다운 리셋
            cooldown.RemainingTime = stats.AttackSpeed > 0
                ? 1.0f / stats.AttackSpeed
                : 1.0f;
        }
    }
}
