using Unity.Burst;
using Unity.Entities;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Transforms;

namespace Shared
{
    /// <summary>
    /// Y축 위치를 초기 값으로 고정하는 시스템
    /// - 물리 충돌 해결로 인한 y축 떠오름 방지
    /// - Physics 시뮬레이션 후 실행
    /// - Burst + Job으로 최적화
    /// </summary>
    [UpdateInGroup(typeof(PhysicsSystemGroup))]
    [UpdateAfter(typeof(PhysicsSimulationGroup))]
    [BurstCompile]
    public partial struct YPositionLockSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.Dependency = new YPositionLockJob().ScheduleParallel(state.Dependency);
        }
    }

    /// <summary>
    /// Y축 위치 고정 Job
    /// - LockedYPosition이 있는 모든 엔티티 처리
    /// - LocalTransform.Position.y를 초기 값으로 복원
    /// - PhysicsVelocity.Linear.y를 0으로 설정
    /// </summary>
    [BurstCompile]
    public partial struct YPositionLockJob : IJobEntity
    {
        private void Execute(
            ref LocalTransform transform,
            ref PhysicsVelocity velocity,
            in LockedYPosition lockedY)
        {
            // Y축 위치 복원
            transform.Position.y = lockedY.Value;

            // Y축 속도 제거 (떠오름 방지)
            velocity.Linear.y = 0f;
        }
    }
}
