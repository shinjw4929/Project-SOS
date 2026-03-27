using Unity.Entities;
using Shared;

namespace Server
{
    /// <summary>
    /// UnitActionState/EnemyState 변화를 감지하여 VATAnimationState.CurrentClipIndex를 갱신한다.
    /// VAT 적용 대상(Hero, EnemySmall, EnemyFlying)만 VATAnimationState를 보유하므로 자연 필터링.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(FixedStepSimulationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial struct VATAnimationStateUpdateSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<VATAnimationState>();
        }

        public void OnUpdate(ref SystemState state)
        {
            float elapsedTime = (float)SystemAPI.Time.ElapsedTime;

            // 유닛 클립 인덱스 갱신 (Hero만 VATAnimationState 보유)
            foreach (var (actionState, animState) in
                SystemAPI.Query<RefRO<UnitActionState>, RefRW<VATAnimationState>>())
            {
                byte newClip = GetUnitClipIndex(actionState.ValueRO.State);
                if (newClip == animState.ValueRO.CurrentClipIndex) continue;

                animState.ValueRW.CurrentClipIndex = newClip;
                animState.ValueRW.AnimStartTime = elapsedTime;
            }

            // 적 클립 인덱스 갱신 (EnemySmall/EnemyFlying만 VATAnimationState 보유)
            foreach (var (enemyState, animState) in
                SystemAPI.Query<RefRO<EnemyState>, RefRW<VATAnimationState>>())
            {
                byte newClip = GetEnemyClipIndex(enemyState.ValueRO.CurrentState);
                if (newClip == animState.ValueRO.CurrentClipIndex) continue;

                animState.ValueRW.CurrentClipIndex = newClip;
                animState.ValueRW.AnimStartTime = elapsedTime;
            }
        }

        // Hero(LittleSquirrel) 베이킹 순서: [0]Idle, [1]Walk, [2]Eat, [3]Sleep02(사망)
        private static byte GetUnitClipIndex(Action action) => action switch
        {
            Action.Idle      => 0,
            Action.Moving    => 1, // Walk
            Action.Working   => 2, // Eat
            Action.Attacking => 0, // 클립 부재 -> Idle 폴백 (전투 기울임으로 대체)
            Action.Dying     => 3, // Sleep02
            Action.Dead      => 3,
            Action.Disabled  => 0,
            _                => 0,
        };

        // Ghost 베이킹 순서: [0]ghost_idle, [1]ghost_run, [2]ghost_attack, [3]ghost_dissolve
        private static byte GetEnemyClipIndex(EnemyContext ctx) => ctx switch
        {
            EnemyContext.Idle      => 0,
            EnemyContext.Dormant   => 0,
            EnemyContext.Wandering => 1, // ghost_run
            EnemyContext.Chasing   => 1, // ghost_run
            EnemyContext.Attacking => 2, // ghost_attack
            EnemyContext.Dying     => 3, // ghost_dissolve
            EnemyContext.Dead      => 3,
            EnemyContext.Disabled  => 0,
            _                      => 0,
        };
    }
}
