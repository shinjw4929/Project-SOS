using Shared;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

namespace Server
{
    /// <summary>
    /// 근접 공격 시스템
    /// - 유닛과 적 모두 AggroTarget 기반으로 동일하게 처리
    /// - 거리 기반 히트 판정 (RTS 스타일)
    /// - 쿨다운 관리 및 데미지 적용
    /// </summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(EnemyTargetSystem))]
    [UpdateBefore(typeof(ServerDeathSystem))]
    public partial struct MeleeAttackSystem : ISystem
    {
        // 타겟 정보 조회용 Lookup
        private ComponentLookup<LocalTransform> _transformLookup;
        private ComponentLookup<Health> _healthLookup;
        private ComponentLookup<Defense> _defenseLookup;
        private ComponentLookup<Team> _teamLookup;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _transformLookup = state.GetComponentLookup<LocalTransform>(true);
            _healthLookup = state.GetComponentLookup<Health>(false); // 쓰기 필요
            _defenseLookup = state.GetComponentLookup<Defense>(true);
            _teamLookup = state.GetComponentLookup<Team>(true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // 이전에 예약된 Job들(CombatDamageSystem 등)이 완료되기를 기다림
            // Health 컴포넌트에 대한 동시 접근 충돌 방지
            state.CompleteDependency();

            float deltaTime = SystemAPI.Time.DeltaTime;

            // Lookup 업데이트
            _transformLookup.Update(ref state);
            _healthLookup.Update(ref state);
            _defenseLookup.Update(ref state);
            _teamLookup.Update(ref state);

            // =====================================================================
            // 1. 적 유닛 근접 공격 처리
            // =====================================================================
            foreach (var (transform, aggroTarget, combatStats, cooldown, enemyState, myTeam) in
                     SystemAPI.Query<
                         RefRO<LocalTransform>,
                         RefRO<AggroTarget>,
                         RefRO<CombatStats>,
                         RefRW<AttackCooldown>,
                         RefRW<EnemyState>,
                         RefRO<Team>>()
                     .WithAll<EnemyTag>())
            {
                ProcessMeleeAttack(
                    transform.ValueRO,
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
            // 2. 아군 유닛 근접 공격 처리
            // =====================================================================
            foreach (var (transform, aggroTarget, combatStats, cooldown, intentState, actionState, myTeam) in
                     SystemAPI.Query<
                         RefRO<LocalTransform>,
                         RefRW<AggroTarget>,
                         RefRO<CombatStats>,
                         RefRW<AttackCooldown>,
                         RefRW<UnitIntentState>,
                         RefRW<UnitActionState>,
                         RefRO<Team>>()
                     .WithAll<UnitTag>())
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
                    transform.ValueRO,
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
        /// </summary>
        private void ProcessMeleeAttack(
            LocalTransform myTransform,
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
            float distance = math.distance(myTransform.Position, targetPos);

            // 공격 사거리 체크
            if (distance > stats.AttackRange)
            {
                isInRange = false;
                return;
            }

            isInRange = true;

            // 쿨다운 체크
            if (cooldown.RemainingTime > 0) return;

            // 데미지 계산 및 적용
            float defenseValue = _defenseLookup.HasComponent(targetEntity)
                ? _defenseLookup[targetEntity].Value
                : 0f;

            float finalDamage = DamageUtility.CalculateDamage(stats.AttackPower, defenseValue);

            targetHealth.CurrentValue = math.max(0, targetHealth.CurrentValue - finalDamage);
            _healthLookup[targetEntity] = targetHealth;

            // 쿨다운 리셋
            cooldown.RemainingTime = stats.AttackSpeed > 0
                ? 1.0f / stats.AttackSpeed
                : 1.0f;
        }
    }
}
