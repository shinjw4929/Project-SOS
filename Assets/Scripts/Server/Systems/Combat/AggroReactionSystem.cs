using Shared;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Server
{
    /// <summary>
    /// 피격 시 어그로 반응 시스템
    /// - DamageEvent 버퍼에서 Attacker 정보를 읽어 어그로 전환
    /// - N초 동안 어그로 고정 (RemainingLockTime)
    /// - 유닛: 사용자 명령(Move, Build 등) 우선, Idle/AttackMove일 때만 반응
    /// - 적: 항상 피격 시 어그로 반응 활성화
    /// </summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
    [UpdateAfter(typeof(RangedAttackSystem))]
    [UpdateBefore(typeof(DamageApplySystem))]
    public partial struct AggroReactionSystem : ISystem
    {
        private ComponentLookup<LocalTransform> _transformLookup;
        private ComponentLookup<Health> _healthLookup;
        private ComponentLookup<Team> _teamLookup;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _transformLookup = state.GetComponentLookup<LocalTransform>(true);
            _healthLookup = state.GetComponentLookup<Health>(true);
            _teamLookup = state.GetComponentLookup<Team>(true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float deltaTime = SystemAPI.Time.DeltaTime;

            _transformLookup.Update(ref state);
            _healthLookup.Update(ref state);
            _teamLookup.Update(ref state);

            // 적(Enemy) 어그로 반응 Job
            var enemyAggroJob = new EnemyAggroReactionJob
            {
                DeltaTime = deltaTime,
                TransformLookup = _transformLookup,
                HealthLookup = _healthLookup,
                TeamLookup = _teamLookup
            };
            state.Dependency = enemyAggroJob.ScheduleParallel(state.Dependency);

            // 유닛(Unit) 어그로 반응 Job
            var unitAggroJob = new UnitAggroReactionJob
            {
                DeltaTime = deltaTime,
                TransformLookup = _transformLookup,
                HealthLookup = _healthLookup,
                TeamLookup = _teamLookup
            };
            state.Dependency = unitAggroJob.ScheduleParallel(state.Dependency);
        }
    }

    /// <summary>
    /// 적(Enemy) 어그로 반응 Job
    /// - 피격 시 공격자에게 어그로 전환
    /// - RemainingLockTime 동안 어그로 고정
    /// </summary>
    [BurstCompile]
    [WithAll(typeof(EnemyTag))]
    public partial struct EnemyAggroReactionJob : IJobEntity
    {
        public float DeltaTime;

        [ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;
        [ReadOnly] public ComponentLookup<Health> HealthLookup;
        [ReadOnly] public ComponentLookup<Team> TeamLookup;

        private void Execute(
            Entity entity,
            ref AggroTarget aggroTarget,
            ref AggroLock aggroLock,
            ref DynamicBuffer<DamageEvent> damageEvents,
            in EnemyChaseDistance chaseDistance,
            in Team myTeam)
        {
            // 1. RemainingLockTime 감소
            aggroLock.RemainingLockTime = math.max(0f, aggroLock.RemainingLockTime - DeltaTime);

            // 2. DamageEvent 버퍼가 비어있으면 스킵
            if (damageEvents.Length == 0) return;

            // 3. 유효한 Attacker 찾기 (첫 번째 유효한 공격자)
            Entity validAttacker = Entity.Null;
            float3 attackerPos = float3.zero;

            for (int i = 0; i < damageEvents.Length; i++)
            {
                Entity attacker = damageEvents[i].Attacker;
                if (attacker == Entity.Null) continue;

                // Attacker 유효성 체크 (존재 + 살아있음 + 적대)
                if (!TransformLookup.TryGetComponent(attacker, out LocalTransform attackerTransform)) continue;
                if (!HealthLookup.TryGetComponent(attacker, out Health attackerHealth)) continue;
                if (attackerHealth.CurrentValue <= 0) continue;

                // 같은 팀이면 무시
                if (TeamLookup.TryGetComponent(attacker, out Team attackerTeam))
                {
                    if (attackerTeam.teamId == myTeam.teamId) continue;
                }

                validAttacker = attacker;
                attackerPos = attackerTransform.Position;
                break;
            }

            // 4. 유효한 공격자가 없으면 스킵
            if (validAttacker == Entity.Null) return;

            // 5. 어그로 전환 조건 체크
            Entity currentTarget = aggroTarget.TargetEntity;
            bool canSwitchTarget = false;

            if (currentTarget == Entity.Null)
            {
                // 타겟 없음 → 즉시 전환
                canSwitchTarget = true;
            }
            else if (aggroLock.RemainingLockTime <= 0f)
            {
                // 고정 시간 만료 → 전환 가능
                canSwitchTarget = true;
            }
            // else: 고정 시간 남음 → 전환 불가

            // 6. 어그로 전환
            if (canSwitchTarget)
            {
                aggroTarget.TargetEntity = validAttacker;
                aggroTarget.LastTargetPosition = attackerPos;

                aggroLock.LockedTarget = validAttacker;
                aggroLock.RemainingLockTime = aggroLock.LockDuration;
            }
        }
    }

    /// <summary>
    /// 유닛(Unit) 어그로 반응 Job
    /// - 사용자 명령(Move, Build, Gather, Hold, Patrol, Attack) 우선
    /// - Idle/AttackMove 상태에서만 피격 어그로 활성화
    /// </summary>
    [BurstCompile]
    [WithAll(typeof(UnitTag))]
    public partial struct UnitAggroReactionJob : IJobEntity
    {
        public float DeltaTime;

        [ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;
        [ReadOnly] public ComponentLookup<Health> HealthLookup;
        [ReadOnly] public ComponentLookup<Team> TeamLookup;

        private void Execute(
            Entity entity,
            ref AggroTarget aggroTarget,
            ref AggroLock aggroLock,
            ref UnitIntentState intentState,
            ref DynamicBuffer<DamageEvent> damageEvents,
            in Team myTeam)
        {
            // 1. RemainingLockTime 감소
            aggroLock.RemainingLockTime = math.max(0f, aggroLock.RemainingLockTime - DeltaTime);

            // 2. DamageEvent 버퍼가 비어있으면 스킵
            if (damageEvents.Length == 0) return;

            // 3. 사용자 명령 우선 체크
            Intent currentIntent = intentState.State;

            // 사용자 명령 상태면 어그로 반응 무시
            // Move, Build, Gather, Hold, Patrol = 사용자가 명시적으로 내린 명령
            // Attack = 사용자가 직접 타겟 지정 (어그로 반응 안함)
            if (currentIntent == Intent.Move ||
                currentIntent == Intent.Build ||
                currentIntent == Intent.Gather ||
                currentIntent == Intent.Hold ||
                currentIntent == Intent.Patrol ||
                currentIntent == Intent.Attack)
            {
                return;
            }

            // Idle 또는 AttackMove 상태에서만 어그로 반응
            if (currentIntent != Intent.Idle && currentIntent != Intent.AttackMove)
            {
                return;
            }

            // 4. 유효한 Attacker 찾기 (첫 번째 유효한 공격자)
            Entity validAttacker = Entity.Null;
            float3 attackerPos = float3.zero;

            for (int i = 0; i < damageEvents.Length; i++)
            {
                Entity attacker = damageEvents[i].Attacker;
                if (attacker == Entity.Null) continue;

                // Attacker 유효성 체크 (존재 + 살아있음 + 적대)
                if (!TransformLookup.TryGetComponent(attacker, out LocalTransform attackerTransform)) continue;
                if (!HealthLookup.TryGetComponent(attacker, out Health attackerHealth)) continue;
                if (attackerHealth.CurrentValue <= 0) continue;

                // 같은 팀이면 무시
                if (TeamLookup.TryGetComponent(attacker, out Team attackerTeam))
                {
                    if (attackerTeam.teamId == myTeam.teamId) continue;
                }

                validAttacker = attacker;
                attackerPos = attackerTransform.Position;
                break;
            }

            // 5. 유효한 공격자가 없으면 스킵
            if (validAttacker == Entity.Null) return;

            // 6. 어그로 전환 조건 체크
            Entity currentTarget = aggroTarget.TargetEntity;
            bool canSwitchTarget = false;

            if (currentTarget == Entity.Null)
            {
                // 타겟 없음 → 즉시 전환
                canSwitchTarget = true;
            }
            else if (aggroLock.RemainingLockTime <= 0f)
            {
                // 고정 시간 만료 → 전환 가능
                canSwitchTarget = true;
            }
            // else: 고정 시간 남음 → 전환 불가

            // 7. 어그로 전환
            if (canSwitchTarget)
            {
                aggroTarget.TargetEntity = validAttacker;
                aggroTarget.LastTargetPosition = attackerPos;

                aggroLock.LockedTarget = validAttacker;
                aggroLock.RemainingLockTime = aggroLock.LockDuration;

                // Intent를 Attack으로 변경
                intentState.State = Intent.Attack;
                intentState.TargetEntity = validAttacker;
                intentState.TargetLastKnownPos = attackerPos;
            }
        }
    }
}
