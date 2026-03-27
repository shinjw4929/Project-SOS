using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Shared;

namespace Client
{
    /// <summary>
    /// Attacking 상태의 유닛/적에 전방 기울임(pitch) 시각 효과를 적용한다.
    /// VAT 유무와 무관하게 전체 유닛/적 대상.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(VATAnimationPlaybackSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct CombatTiltSystem : ISystem
    {
        private const float DefaultTiltAngle = 0.3f; // ~17도
        private const float DefaultTiltSpeed = 8.0f;

        public void OnUpdate(ref SystemState state)
        {
            float tiltAngle = DefaultTiltAngle;
            float tiltSpeed = DefaultTiltSpeed;

            if (SystemAPI.TryGetSingleton<GameSettings>(out var gs))
            {
                tiltAngle = gs.CombatTiltAngle;
                tiltSpeed = gs.CombatTiltSpeed;
            }

            float deltaTime = SystemAPI.Time.DeltaTime;

            new UnitTiltJob
            {
                TiltAngle = tiltAngle,
                TiltSpeed = tiltSpeed,
                DeltaTime = deltaTime
            }.ScheduleParallel();

            new EnemyTiltJob
            {
                TiltAngle = tiltAngle,
                TiltSpeed = tiltSpeed,
                DeltaTime = deltaTime
            }.ScheduleParallel();
        }
    }

    [BurstCompile]
    public partial struct UnitTiltJob : IJobEntity
    {
        public float TiltAngle;
        public float TiltSpeed;
        public float DeltaTime;

        void Execute(in UnitActionState actionState, ref LocalTransform transform)
        {
            bool isAttacking = actionState.State == Action.Attacking;
            ApplyTilt(ref transform, isAttacking);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ApplyTilt(ref LocalTransform transform, bool isAttacking)
        {
            float targetPitch = isAttacking ? TiltAngle : 0f;

            float3 forward = math.mul(transform.Rotation, math.forward());
            float currentYaw = math.atan2(forward.x, forward.z);
            float currentPitch = math.asin(math.clamp(-forward.y, -1f, 1f));

            float newPitch = math.lerp(currentPitch, targetPitch, math.saturate(TiltSpeed * DeltaTime));

            transform.Rotation = math.mul(
                quaternion.RotateY(currentYaw),
                quaternion.RotateX(newPitch));
        }
    }

    [BurstCompile]
    public partial struct EnemyTiltJob : IJobEntity
    {
        public float TiltAngle;
        public float TiltSpeed;
        public float DeltaTime;

        void Execute(in EnemyState enemyState, ref LocalTransform transform)
        {
            bool isAttacking = enemyState.CurrentState == EnemyContext.Attacking;
            ApplyTilt(ref transform, isAttacking);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ApplyTilt(ref LocalTransform transform, bool isAttacking)
        {
            float targetPitch = isAttacking ? TiltAngle : 0f;

            float3 forward = math.mul(transform.Rotation, math.forward());
            float currentYaw = math.atan2(forward.x, forward.z);
            float currentPitch = math.asin(math.clamp(-forward.y, -1f, 1f));

            float newPitch = math.lerp(currentPitch, targetPitch, math.saturate(TiltSpeed * DeltaTime));

            transform.Rotation = math.mul(
                quaternion.RotateY(currentYaw),
                quaternion.RotateX(newPitch));
        }
    }
}
