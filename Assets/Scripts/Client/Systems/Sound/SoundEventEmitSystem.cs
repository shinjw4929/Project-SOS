using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using Shared;

namespace Client
{
    /// <summary>
    /// UnitActionState/EnemyState 변화를 감지하여 SoundEvent 버퍼에 이벤트를 추가한다.
    /// VAT 유무와 무관하게 전체 유닛/적 대상.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(VATAnimationPlaybackSystem))]
    [UpdateBefore(typeof(TeamColorSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct SoundEventEmitSystem : ISystem
    {
        private ComponentLookup<RangedUnitTag> rangedUnitLookup;
        private ComponentLookup<RangedEnemyTag> rangedEnemyLookup;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<SoundEventState>();
            rangedUnitLookup = state.GetComponentLookup<RangedUnitTag>(true);
            rangedEnemyLookup = state.GetComponentLookup<RangedEnemyTag>(true);
        }

        public void OnUpdate(ref SystemState state)
        {
            // PreviousActionState 미부착 유닛에 초기화 (구조적 변경 → Lookup 갱신 전에 수행)
            InitializePreviousStates(ref state);

            rangedUnitLookup.Update(ref state);
            rangedEnemyLookup.Update(ref state);

            var soundEntity = SystemAPI.GetSingletonEntity<SoundEventState>();
            var soundBuffer = state.EntityManager.GetBuffer<SoundEvent>(soundEntity);

            // 유닛 사운드 이벤트
            foreach (var (actionState, prevAction, ltw, entity) in
                SystemAPI.Query<RefRO<UnitActionState>, RefRW<PreviousActionState>, RefRO<LocalToWorld>>()
                    .WithEntityAccess())
            {
                var current = actionState.ValueRO.State;
                var previous = prevAction.ValueRO.Value;

                if (current == previous) continue;

                bool isRanged = rangedUnitLookup.HasComponent(entity);
                SoundType soundType = GetUnitSoundType(current, isRanged);

                if (soundType != SoundType.None)
                    soundBuffer.Add(new SoundEvent { Type = soundType, Position = ltw.ValueRO.Position, Volume = 1.0f });

                prevAction.ValueRW.Value = current;
            }

            // 적 사운드 이벤트
            foreach (var (enemyState, prevCtx, ltw, entity) in
                SystemAPI.Query<RefRO<EnemyState>, RefRW<PreviousEnemyContext>, RefRO<LocalToWorld>>()
                    .WithEntityAccess())
            {
                var current = enemyState.ValueRO.CurrentState;
                var previous = prevCtx.ValueRO.Value;

                if (current == previous) continue;

                bool isRanged = rangedEnemyLookup.HasComponent(entity);
                SoundType soundType = GetEnemySoundType(current, isRanged);

                if (soundType != SoundType.None)
                    soundBuffer.Add(new SoundEvent { Type = soundType, Position = ltw.ValueRO.Position, Volume = 1.0f });

                prevCtx.ValueRW.Value = current;
            }
        }

        private void InitializePreviousStates(ref SystemState state)
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            bool hasNew = false;

            foreach (var (actionState, entity) in
                SystemAPI.Query<RefRO<UnitActionState>>()
                    .WithNone<PreviousActionState>()
                    .WithEntityAccess())
            {
                ecb.AddComponent(entity, new PreviousActionState { Value = actionState.ValueRO.State });
                hasNew = true;
            }

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

        private static SoundType GetUnitSoundType(Action action, bool isRanged) => action switch
        {
            Action.Attacking => isRanged ? SoundType.RangedShot : SoundType.MeleeHit,
            Action.Dying     => SoundType.UnitDeath,
            Action.Working   => SoundType.WorkerGather,
            _                => SoundType.None,
        };

        private static SoundType GetEnemySoundType(EnemyContext ctx, bool isRanged) => ctx switch
        {
            EnemyContext.Attacking => isRanged ? SoundType.RangedShot : SoundType.MeleeHit,
            EnemyContext.Dying     => SoundType.EnemyDeath,
            _                      => SoundType.None,
        };
    }
}
