using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Shared;

namespace Client
{
    /// <summary>
    /// UnitActionState/EnemyState 변화를 감지하여 SoundEvent 버퍼에 이벤트를 추가한다.
    /// - 상태 전이: Dying, Working 등은 상태 변화 시 1회 발생
    /// - 공격: Attacking 상태 유지 중 CombatStats.AttackSpeed 간격으로 반복 발생
    /// - 스폰: 유닛 최초 감지 시 UnitSpawn 1회 발생
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(VATAnimationPlaybackSystem))]
    [UpdateBefore(typeof(TeamColorSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct SoundEventEmitSystem : ISystem
    {
        private ComponentLookup<RangedUnitTag> rangedUnitLookup;
        private ComponentLookup<RangedEnemyTag> rangedEnemyLookup;
        private ComponentLookup<CombatStats> combatStatsLookup;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<SoundEventState>();
            rangedUnitLookup = state.GetComponentLookup<RangedUnitTag>(true);
            rangedEnemyLookup = state.GetComponentLookup<RangedEnemyTag>(true);
            combatStatsLookup = state.GetComponentLookup<CombatStats>(true);
        }

        public void OnUpdate(ref SystemState state)
        {
            // 1단계: PreviousState 미부착 엔티티 초기화 + 스폰 위치 수집
            var spawnPositions = new NativeList<float3>(Allocator.Temp);
            InitializePreviousStates(ref state, ref spawnPositions);

            rangedUnitLookup.Update(ref state);
            rangedEnemyLookup.Update(ref state);
            combatStatsLookup.Update(ref state);

            var soundEntity = SystemAPI.GetSingletonEntity<SoundEventState>();
            var soundBuffer = SystemAPI.GetBuffer<SoundEvent>(soundEntity);
            float deltaTime = SystemAPI.Time.DeltaTime;

            // 2단계: 스폰 사운드
            for (int i = 0; i < spawnPositions.Length; i++)
            {
                soundBuffer.Add(new SoundEvent
                {
                    Type = SoundType.UnitSpawn,
                    Position = spawnPositions[i],
                    Volume = 1.0f
                });
            }
            spawnPositions.Dispose();

            // 3단계: 유닛 사운드 이벤트
            foreach (var (actionState, prevAction, ltw, entity) in
                SystemAPI.Query<RefRO<UnitActionState>, RefRW<PreviousActionState>, RefRO<LocalToWorld>>()
                    .WithEntityAccess())
            {
                var current = actionState.ValueRO.State;
                var previous = prevAction.ValueRO.Value;

                if (current != previous)
                {
                    // 상태 전이 → 1회 사운드
                    bool isRanged = rangedUnitLookup.HasComponent(entity);
                    SoundType soundType = GetUnitSoundType(current, isRanged);

                    if (soundType != SoundType.None)
                        soundBuffer.Add(new SoundEvent { Type = soundType, Position = ltw.ValueRO.Position, Volume = 1.0f });

                    // Attacking 진입 시 타이머 초기화 (첫 타격은 상태 전이에서 이미 발생)
                    if (current == Action.Attacking)
                    {
                        float interval = GetAttackInterval(entity);
                        prevAction.ValueRW.AttackSoundTimer = interval;
                    }

                    prevAction.ValueRW.Value = current;
                }
                else if (current == Action.Attacking)
                {
                    // Attacking 유지 중 → 타이머 기반 반복 사운드
                    prevAction.ValueRW.AttackSoundTimer -= deltaTime;
                    if (prevAction.ValueRW.AttackSoundTimer <= 0)
                    {
                        bool isRanged = rangedUnitLookup.HasComponent(entity);
                        SoundType soundType = isRanged ? SoundType.RangedShot : SoundType.MeleeHit;
                        soundBuffer.Add(new SoundEvent { Type = soundType, Position = ltw.ValueRO.Position, Volume = 1.0f });

                        float interval = GetAttackInterval(entity);
                        prevAction.ValueRW.AttackSoundTimer = interval;
                    }
                }
            }

            // 4단계: 적 사운드 이벤트
            foreach (var (enemyState, prevCtx, ltw, entity) in
                SystemAPI.Query<RefRO<EnemyState>, RefRW<PreviousEnemyContext>, RefRO<LocalToWorld>>()
                    .WithEntityAccess())
            {
                var current = enemyState.ValueRO.CurrentState;
                var previous = prevCtx.ValueRO.Value;

                if (current != previous)
                {
                    bool isRanged = rangedEnemyLookup.HasComponent(entity);
                    SoundType soundType = GetEnemySoundType(current, isRanged);

                    if (soundType != SoundType.None)
                        soundBuffer.Add(new SoundEvent { Type = soundType, Position = ltw.ValueRO.Position, Volume = 1.0f });

                    if (current == EnemyContext.Attacking)
                    {
                        float interval = GetAttackInterval(entity);
                        prevCtx.ValueRW.AttackSoundTimer = interval;
                    }

                    prevCtx.ValueRW.Value = current;
                }
                else if (current == EnemyContext.Attacking)
                {
                    prevCtx.ValueRW.AttackSoundTimer -= deltaTime;
                    if (prevCtx.ValueRW.AttackSoundTimer <= 0)
                    {
                        bool isRanged = rangedEnemyLookup.HasComponent(entity);
                        SoundType soundType = isRanged ? SoundType.EnemyRangedShot : SoundType.EnemyMeleeHit;
                        soundBuffer.Add(new SoundEvent { Type = soundType, Position = ltw.ValueRO.Position, Volume = 1.0f });

                        float interval = GetAttackInterval(entity);
                        prevCtx.ValueRW.AttackSoundTimer = interval;
                    }
                }
            }
        }

        private void InitializePreviousStates(ref SystemState state, ref NativeList<float3> spawnPositions)
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            bool hasNew = false;

            // 유닛 초기화 + 스폰 감지
            foreach (var (actionState, ltw, entity) in
                SystemAPI.Query<RefRO<UnitActionState>, RefRO<LocalToWorld>>()
                    .WithNone<PreviousActionState>()
                    .WithEntityAccess())
            {
                ecb.AddComponent(entity, new PreviousActionState { Value = actionState.ValueRO.State });
                spawnPositions.Add(ltw.ValueRO.Position);
                hasNew = true;
            }

            // 적 초기화 (스폰 사운드 없음)
            foreach (var (enemyState, entity) in
                SystemAPI.Query<RefRO<EnemyState>>()
                    .WithNone<PreviousEnemyContext>()
                    .WithEntityAccess())
            {
                ecb.AddComponent(entity, new PreviousEnemyContext { Value = enemyState.ValueRO.CurrentState });
                hasNew = true;
            }

            if (hasNew)
                ecb.Playback(state.EntityManager);

            ecb.Dispose();
        }

        private float GetAttackInterval(Entity entity)
        {
            if (combatStatsLookup.TryGetComponent(entity, out var combatStats) && combatStats.AttackSpeed > 0)
                return 1f / combatStats.AttackSpeed;
            return 1f;
        }

        private static SoundType GetUnitSoundType(Action action, bool isRanged) => action switch
        {
            Action.Attacking => isRanged ? SoundType.RangedShot : SoundType.MeleeHit,
            Action.Dying     => SoundType.UnitDeath,
            Action.Working   => SoundType.WorkerGather,
            _                => SoundType.None,
        };

        private static SoundType GetEnemySoundType(EnemyContext ctx, bool isRanged) => ctx switch
        {
            EnemyContext.Attacking => isRanged ? SoundType.EnemyRangedShot : SoundType.EnemyMeleeHit,
            EnemyContext.Dying     => SoundType.EnemyDeath,
            _                      => SoundType.None,
        };
    }
}
