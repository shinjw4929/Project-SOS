using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using Unity.NetCode;

namespace Shared
{
    /// <summary>
    /// PhysicsVelocity 기반 가속도/감속도 이동 시스템
    /// - 물리 엔진이 충돌 처리 담당
    /// - 유닛/적 모두 처리
    /// </summary>
    [UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
    [BurstCompile]
    public partial struct PredictedMovementSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkTime>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float deltaTime = SystemAPI.Time.DeltaTime;

            var job = new MoveJob
            {
                DeltaTime = deltaTime
            };

            state.Dependency = job.ScheduleParallel(state.Dependency);
        }
    }

    [BurstCompile]
    public partial struct MoveJob : IJobEntity
    {
        public float DeltaTime;

        /// <summary>
        /// PhysicsVelocity 기반 이동 처리
        /// - MovementWaypoints가 활성화된 엔티티만 자동으로 처리 (IEnableableComponent)
        /// </summary>
        private void Execute(
            ref LocalTransform transform,
            ref PhysicsVelocity velocity,
            ref MovementWaypoints waypoints,
            in MovementDynamics dynamics)
        {
            float3 currentPos = transform.Position;
            float3 targetPos = waypoints.Current;

            // Y축 고정 (RTS 평면 이동)
            targetPos.y = currentPos.y;

            float3 toTarget = targetPos - currentPos;
            float distanceSq = math.lengthsq(toTarget);
            float distance = math.sqrt(distanceSq);

            // ==========================================================
            // 1. 코너링 (웨이포인트 스위칭)
            // ==========================================================
            const float CornerRadius = 0.5f;
            if (waypoints.HasNext && distance < CornerRadius)
            {
                waypoints.Current = waypoints.Next;
                waypoints.HasNext = false;

                // 타겟 재계산
                targetPos = waypoints.Current;
                targetPos.y = currentPos.y;
                toTarget = targetPos - currentPos;
                distanceSq = math.lengthsq(toTarget);
                distance = math.sqrt(distanceSq);
            }

            // ==========================================================
            // 2. 도착 근처면 완전 정지 (진동 방지)
            // ==========================================================
            const float ArrivalThreshold = 0.3f;
            if (!waypoints.HasNext && distance < ArrivalThreshold)
            {
                velocity.Linear = float3.zero;
                velocity.Angular = float3.zero; // 각속도도 정지
                return; // 회전도 하지 않음
            }

            // 이동할 필요가 없으면 정지
            if (distance <= 0.001f)
            {
                velocity.Linear = float3.zero;
                return;
            }

            // ==========================================================
            // 3. 목표 속도 계산 (Arrival 감속 로직)
            // ==========================================================
            float targetSpeed = dynamics.MaxSpeed;

            // 최종 목적지인 경우만 감속 (중간 웨이포인트는 MaxSpeed 유지)
            if (!waypoints.HasNext)
            {
                // 감속 시작 거리: v^2 = 2as -> s = v^2 / (2a)
                float slowingDistance = (dynamics.MaxSpeed * dynamics.MaxSpeed) / (2f * dynamics.Deceleration);

                if (distance < slowingDistance)
                {
                    // 거리에 비례하여 속도 감소 (선형 감속)
                    targetSpeed = dynamics.MaxSpeed * (distance / slowingDistance);
                    // 최소 속도: ArrivalThreshold 이상의 거리에서만 적용
                    targetSpeed = math.max(targetSpeed, 0.5f);
                }
            }

            // ==========================================================
            // 4. 가속/감속 적용 (현재 속도 -> 목표 속도)
            // ==========================================================
            float3 currentVelocity = velocity.Linear;
            float currentSpeed = math.length(currentVelocity);

            float speedDiff = targetSpeed - currentSpeed;
            float accelToUse = speedDiff > 0f ? dynamics.Acceleration : dynamics.Deceleration;

            // 새 속도 계산 (Clamp로 오버슈팅 방지)
            float newSpeed = currentSpeed + math.sign(speedDiff) * math.min(math.abs(speedDiff), accelToUse * DeltaTime);
            newSpeed = math.max(0f, newSpeed);

            // ==========================================================
            // 5. 방향 계산 및 속도 적용
            // ==========================================================
            float3 moveDir = math.normalizesafe(toTarget);
            float3 finalVelocity = moveDir * newSpeed;

            // Y축 속도 제거 (평면 이동만)
            finalVelocity.y = 0f;

            // ==========================================================
            // 6. 정지 보정 (Snap) - 물리 엔진 특성상 0에 수렴 어려움
            // ==========================================================
            if (!waypoints.HasNext && distance < 0.02f && newSpeed < 0.3f)
            {
                finalVelocity = float3.zero;
            }

            velocity.Linear = finalVelocity;

            // ==========================================================
            // 7. 회전 (바라보는 방향) - Slerp로 부드럽게 보간
            // ==========================================================
            if (math.lengthsq(moveDir) > 0.001f)
            {
                quaternion targetRotation = quaternion.LookRotationSafe(moveDir, math.up());
                float t = math.saturate(DeltaTime * dynamics.RotationSpeed);
                transform.Rotation = math.slerp(transform.Rotation, targetRotation, t);
            }
        }
    }
}
