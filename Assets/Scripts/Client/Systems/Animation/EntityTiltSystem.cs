using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Shared;

namespace Client
{
    /// <summary>
    /// 유닛/적의 상태 기반 기울임(pitch) 시각 효과를 적용한다.
    /// - Attacking: timer 기반 swing-return 사이클 (CombatStats 보유 엔티티만)
    /// - Dying: 점진적 전방 기울임 (DeathTiltAngle까지, 모든 유닛/적 대상)
    /// PostTransformMatrix를 사용하여 Ghost 동기화(LocalTransform 덮어쓰기)와 독립적으로 동작.
    /// VAT 유무 무관, 전체 유닛/적 대상.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(VATAnimationPlaybackSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct EntityTiltSystem : ISystem
    {
        private const float DefaultTiltAngle = 0.3f;
        private const float DefaultTiltSpeed = 8.0f;
        private const float DefaultSwingRatio = 0.4f;
        private const float DefaultDeathTiltAngle = 1.05f;
        private const float DefaultDeathDuration = 0.5f;

        private EntityQuery _uninitUnitQuery;
        private EntityQuery _uninitEnemyQuery;
        [ReadOnly] private ComponentLookup<CombatStats> _combatStatsLookup;

        public void OnCreate(ref SystemState state)
        {
            _uninitUnitQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<UnitActionState>()
                .WithNone<CombatTiltTimer>()
                .Build(ref state);
            _uninitEnemyQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<EnemyState, CombatStats>()
                .WithNone<CombatTiltTimer>()
                .Build(ref state);
            _combatStatsLookup = state.GetComponentLookup<CombatStats>(true);
        }

        public void OnUpdate(ref SystemState state)
        {
            float tiltAngle = DefaultTiltAngle;
            float tiltSpeed = DefaultTiltSpeed;
            float swingRatio = DefaultSwingRatio;
            float deathTiltAngle = DefaultDeathTiltAngle;
            float deathDuration = DefaultDeathDuration;

            if (SystemAPI.TryGetSingleton<GameSettings>(out var gs))
            {
                tiltAngle = gs.CombatTiltAngle;
                tiltSpeed = gs.CombatTiltSpeed;
                swingRatio = gs.CombatTiltSwingRatio;
                deathTiltAngle = gs.DeathTiltAngle;
                deathDuration = gs.DeathDuration;
            }

            float deltaTime = SystemAPI.Time.DeltaTime;

            _combatStatsLookup.Update(ref state);

            InitializeTiltComponents(ref state);

            new UnitTiltJob
            {
                TiltAngle = tiltAngle,
                ReturnSpeed = tiltSpeed,
                SwingRatio = swingRatio,
                DeltaTime = deltaTime,
                DeathTiltAngle = deathTiltAngle,
                DeathDuration = deathDuration,
                CombatStatsLookup = _combatStatsLookup
            }.ScheduleParallel();

            new EnemyTiltJob
            {
                TiltAngle = tiltAngle,
                ReturnSpeed = tiltSpeed,
                SwingRatio = swingRatio,
                DeltaTime = deltaTime,
                DeathTiltAngle = deathTiltAngle,
                DeathDuration = deathDuration
            }.ScheduleParallel();
        }

        private void InitializeTiltComponents(ref SystemState state)
        {
            if (_uninitUnitQuery.IsEmptyIgnoreFilter && _uninitEnemyQuery.IsEmptyIgnoreFilter)
                return;

            var ecb = new EntityCommandBuffer(Allocator.Temp);
            bool hasNew = false;

            foreach (var (_, entity) in
                SystemAPI.Query<RefRO<UnitActionState>>()
                    .WithNone<CombatTiltTimer>()
                    .WithEntityAccess())
            {
                ecb.AddComponent(entity, new CombatTiltTimer());
                ecb.AddComponent(entity, new PostTransformMatrix { Value = float4x4.identity });
                hasNew = true;
            }

            foreach (var (_, _, entity) in
                SystemAPI.Query<RefRO<EnemyState>, RefRO<CombatStats>>()
                    .WithNone<CombatTiltTimer>()
                    .WithEntityAccess())
            {
                ecb.AddComponent(entity, new CombatTiltTimer());
                ecb.AddComponent(entity, new PostTransformMatrix { Value = float4x4.identity });
                hasNew = true;
            }

            if (hasNew)
                ecb.Playback(state.EntityManager);

            ecb.Dispose();
        }
    }

    /// <summary>
    /// 기울임 계산 공용 유틸리티. UnitTiltJob/EnemyTiltJob에서 공유.
    /// </summary>
    [BurstCompile]
    public static class TiltUtility
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ComputeSwingTilt(ref CombatTiltTimer timer, ref PostTransformMatrix postMatrix,
            bool isAttacking, float attackSpeed,
            float tiltAngle, float returnSpeed, float swingRatio, float deltaTime)
        {
            byte wasAttacking = timer.WasAttacking;

            if (isAttacking)
            {
                if (wasAttacking == 0)
                    timer.Timer = 0f;

                timer.Timer += deltaTime;

                float period = 1f / math.max(attackSpeed, 0.01f);
                float cycleTime = math.fmod(timer.Timer, period);
                float swingDuration = period * swingRatio;

                timer.CurrentTilt = cycleTime < swingDuration
                    ? math.sin(cycleTime / swingDuration * math.PI)
                    : 0f;
            }
            else
            {
                timer.CurrentTilt = math.max(0f, timer.CurrentTilt - returnSpeed * deltaTime);
                timer.Timer = 0f;
            }

            timer.WasAttacking = isAttacking ? (byte)1 : (byte)0;
            timer.WasDying = 0;

            float pitch = tiltAngle * timer.CurrentTilt;
            postMatrix.Value = float4x4.RotateX(pitch);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ComputeDeathTilt(ref CombatTiltTimer timer, ref PostTransformMatrix postMatrix,
            float deathTiltAngle, float deathDuration, float deltaTime)
        {
            if (timer.WasDying == 0)
            {
                timer.DeathTiltElapsed = 0f;
                timer.CurrentTilt = 0f;
            }

            timer.DeathTiltElapsed += deltaTime;
            timer.WasDying = 1;

            float progress = math.saturate(timer.DeathTiltElapsed / math.max(deathDuration, 0.1f));
            float pitch = deathTiltAngle * progress;

            postMatrix.Value = float4x4.RotateX(pitch);
        }
    }

    [BurstCompile]
    public partial struct UnitTiltJob : IJobEntity
    {
        public float TiltAngle;
        public float ReturnSpeed;
        public float SwingRatio;
        public float DeltaTime;
        public float DeathTiltAngle;
        public float DeathDuration;
        [ReadOnly] public ComponentLookup<CombatStats> CombatStatsLookup;

        void Execute(Entity entity, in UnitActionState actionState,
            ref CombatTiltTimer tiltTimer, ref PostTransformMatrix postMatrix)
        {
            if (actionState.State == Action.Dying)
            {
                TiltUtility.ComputeDeathTilt(ref tiltTimer, ref postMatrix, DeathTiltAngle, DeathDuration, DeltaTime);
                return;
            }

            float attackSpeed = CombatStatsLookup.TryGetComponent(entity, out var combatStats)
                ? combatStats.AttackSpeed : 0f;
            bool isAttacking = actionState.State == Action.Attacking;
            TiltUtility.ComputeSwingTilt(ref tiltTimer, ref postMatrix, isAttacking, attackSpeed,
                TiltAngle, ReturnSpeed, SwingRatio, DeltaTime);
        }
    }

    [BurstCompile]
    public partial struct EnemyTiltJob : IJobEntity
    {
        public float TiltAngle;
        public float ReturnSpeed;
        public float SwingRatio;
        public float DeltaTime;
        public float DeathTiltAngle;
        public float DeathDuration;

        void Execute(in EnemyState enemyState, in CombatStats combatStats,
            ref CombatTiltTimer tiltTimer, ref PostTransformMatrix postMatrix)
        {
            if (enemyState.CurrentState == EnemyContext.Dying)
            {
                TiltUtility.ComputeDeathTilt(ref tiltTimer, ref postMatrix, DeathTiltAngle, DeathDuration, DeltaTime);
                return;
            }

            bool isAttacking = enemyState.CurrentState == EnemyContext.Attacking;
            TiltUtility.ComputeSwingTilt(ref tiltTimer, ref postMatrix, isAttacking, combatStats.AttackSpeed,
                TiltAngle, ReturnSpeed, SwingRatio, DeltaTime);
        }
    }
}
