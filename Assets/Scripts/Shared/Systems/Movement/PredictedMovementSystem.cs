using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Transforms;
using Unity.NetCode;

namespace Shared
{
    /// <summary>
    /// Kinematic 기반 통합 이동 시스템
    /// - LocalTransform 직접 제어 (IsKinematic)
    /// - Separation (유닛 간 밀어내기) + 건물 충돌 해결을 단일 파이프라인으로 처리
    /// </summary>
    [UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
    [BurstCompile]
    public partial struct PredictedMovementSystem : ISystem
    {
        private ComponentLookup<ObstacleRadius> _obstacleRadiusLookup;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkTime>();
            state.RequireForUpdate<PhysicsWorldSingleton>();
            _obstacleRadiusLookup = state.GetComponentLookup<ObstacleRadius>(true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            _obstacleRadiusLookup.Update(ref state);

            var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
            float deltaTime = SystemAPI.Time.DeltaTime;

            var job = new KinematicMovementJob
            {
                DeltaTime = deltaTime,
                CollisionWorld = physicsWorld.CollisionWorld,
                ObstacleRadiusLookup = _obstacleRadiusLookup,
                SeparationStrength = 8.0f,
                MaxSeparationDistance = 2.0f,
                // Unit(11) + Enemy(12) 레이어 필터
                UnitEnemyFilter = new CollisionFilter
                {
                    BelongsTo = ~0u,
                    CollidesWith = (1u << 11) | (1u << 12),
                    GroupIndex = 0
                },
                // Structure(7) 레이어 필터
                StructureFilter = new CollisionFilter
                {
                    BelongsTo = ~0u,
                    CollidesWith = (1u << 7),
                    GroupIndex = 0
                }
            };

            state.Dependency = job.ScheduleParallel(state.Dependency);
        }
    }

    [BurstCompile]
    public partial struct KinematicMovementJob : IJobEntity
    {
        public float DeltaTime;
        [ReadOnly] public CollisionWorld CollisionWorld;
        [ReadOnly] public ComponentLookup<ObstacleRadius> ObstacleRadiusLookup;
        public float SeparationStrength;
        public float MaxSeparationDistance;
        public CollisionFilter UnitEnemyFilter;
        public CollisionFilter StructureFilter;

        /// <summary>
        /// Kinematic 이동 처리 (LocalTransform 직접 제어)
        /// - MovementWaypoints가 활성화된 엔티티만 처리
        /// </summary>
        private void Execute(
            Entity entity,
            ref LocalTransform transform,
            ref PhysicsVelocity velocity,
            ref MovementWaypoints waypoints,
            in MovementDynamics dynamics,
            in ObstacleRadius obstacleRadius,
            in PhysicsRadius physicsRadius)
        {
            float3 currentPos = transform.Position;

            // ============================================
            // 1. Waypoint 이동 속도 계산
            // ============================================
            float3 targetPos = waypoints.Current;
            targetPos.y = currentPos.y;

            float3 toTarget = targetPos - currentPos;
            float distanceSq = math.lengthsq(toTarget);
            float distance = math.sqrt(distanceSq);

            // 코너링 (웨이포인트 스위칭)
            const float CornerRadius = 0.5f;
            if (waypoints.HasNext && distance < CornerRadius)
            {
                waypoints.Current = waypoints.Next;
                waypoints.HasNext = false;

                targetPos = waypoints.Current;
                targetPos.y = currentPos.y;
                toTarget = targetPos - currentPos;
                distanceSq = math.lengthsq(toTarget);
                distance = math.sqrt(distanceSq);
            }

            // 도착 근처면 완전 정지
            const float ArrivalThreshold = 0.3f;
            if (!waypoints.HasNext && distance < ArrivalThreshold)
            {
                velocity.Linear = float3.zero;
                velocity.Angular = float3.zero;
                return;
            }

            float3 moveDir = math.normalizesafe(toTarget);
            float targetSpeed = CalculateTargetSpeed(dynamics, distance, waypoints.HasNext);

            // 가속/감속 적용
            float3 currentVelocity = velocity.Linear;
            float currentSpeed = math.length(currentVelocity);

            float speedDiff = targetSpeed - currentSpeed;
            float accelToUse = speedDiff > 0f ? dynamics.Acceleration : dynamics.Deceleration;
            float newSpeed = currentSpeed + math.sign(speedDiff) * math.min(math.abs(speedDiff), accelToUse * DeltaTime);
            newSpeed = math.max(0f, newSpeed);

            float3 moveVelocity = moveDir * newSpeed;
            moveVelocity.y = 0f;

            // ============================================
            // 2. Separation (유닛/적 간 밀어내기)
            // ============================================
            float3 separationVelocity = CalculateSeparation(
                currentPos,
                obstacleRadius.Radius,
                entity);

            // ============================================
            // 3. 속도 합산
            // ============================================
            float3 desiredVelocity = moveVelocity + separationVelocity * SeparationStrength;

            // ============================================
            // 4. 건물 충돌 해결 (Wall Sliding)
            // ============================================
            float3 finalVelocity = ResolveStructureCollision(
                currentPos,
                desiredVelocity,
                physicsRadius.Value);

            // ============================================
            // 5. 최종 위치 적용 (Y축 유지)
            // ============================================
            float3 newPos = currentPos + finalVelocity * DeltaTime;
            newPos.y = currentPos.y; // Kinematic이므로 Y축 그대로 유지
            transform.Position = newPos;

            // ============================================
            // 6. PhysicsVelocity 동기화 (네트워크/다른 시스템용)
            // ============================================
            finalVelocity.y = 0f;
            velocity.Linear = finalVelocity;

            // ============================================
            // 7. 회전 처리
            // ============================================
            if (math.lengthsq(moveDir) > 0.001f)
            {
                quaternion targetRotation = quaternion.LookRotationSafe(moveDir, math.up());
                float t = math.saturate(DeltaTime * dynamics.RotationSpeed);
                transform.Rotation = math.slerp(transform.Rotation, targetRotation, t);
            }
        }

        /// <summary>
        /// 목표 속도 계산 (Arrival 감속 로직)
        /// </summary>
        private float CalculateTargetSpeed(in MovementDynamics dynamics, float distance, bool hasNext)
        {
            float targetSpeed = dynamics.MaxSpeed;

            // 최종 목적지인 경우만 감속
            if (!hasNext)
            {
                float slowingDistance = (dynamics.MaxSpeed * dynamics.MaxSpeed) / (2f * dynamics.Deceleration);
                if (distance < slowingDistance)
                {
                    targetSpeed = dynamics.MaxSpeed * (distance / slowingDistance);
                    targetSpeed = math.max(targetSpeed, 0.5f);
                }
            }

            return targetSpeed;
        }

        /// <summary>
        /// Separation 계산 (CollisionWorld.CalculateDistance 활용)
        /// - 주변 유닛/적과의 겹침을 감지하고 밀어내기 벡터 계산
        /// </summary>
        private float3 CalculateSeparation(float3 myPos, float myRadius, Entity myEntity)
        {
            float3 separationForce = float3.zero;
            float searchRadius = myRadius + MaxSeparationDistance;

            // Point로부터 가까운 충돌체 검색
            var hits = new NativeList<DistanceHit>(16, Allocator.Temp);

            var input = new PointDistanceInput
            {
                Position = myPos,
                MaxDistance = searchRadius,
                Filter = UnitEnemyFilter
            };

            if (CollisionWorld.CalculateDistance(input, ref hits))
            {
                for (int i = 0; i < hits.Length; i++)
                {
                    var hit = hits[i];
                    if (hit.Entity == myEntity) continue;

                    float3 toOther = hit.Position - myPos;
                    toOther.y = 0;
                    float dist = math.length(toOther);

                    // ObstacleRadius 기반 겹침 계산
                    float otherRadius = myRadius; // 기본값
                    if (ObstacleRadiusLookup.HasComponent(hit.Entity))
                    {
                        otherRadius = ObstacleRadiusLookup[hit.Entity].Radius;
                    }

                    float combinedRadius = myRadius + otherRadius;

                    if (dist < combinedRadius && dist > 0.001f)
                    {
                        float overlap = combinedRadius - dist;
                        float3 pushDir = -math.normalize(toOther);
                        float strength = math.saturate(overlap / combinedRadius);
                        separationForce += pushDir * overlap * strength;
                    }
                }
            }

            hits.Dispose();
            return separationForce;
        }

        /// <summary>
        /// 건물 충돌 해결 (PointDistance 활용)
        /// - 45도 기준: 수직 충돌 시 멈춤, 사선 충돌 시 미끄러짐
        /// </summary>
        private float3 ResolveStructureCollision(float3 currentPos, float3 velocity, float radius)
        {
            if (math.lengthsq(velocity) < 0.0001f)
            {
                return velocity;
            }

            // 이동 후 예상 위치
            float3 targetPos = currentPos + velocity * DeltaTime;

            // 예상 위치에서 건물과의 거리 검사
            var input = new PointDistanceInput
            {
                Position = targetPos,
                MaxDistance = radius + 0.1f, // 약간의 여유
                Filter = StructureFilter
            };

            if (CollisionWorld.CalculateDistance(input, out DistanceHit hit))
            {
                // 건물과 충돌 예정
                float penetration = radius - hit.Distance;

                if (penetration > 0)
                {
                    // 충돌 방향 (건물 표면에서 유닛을 향하는 방향)
                    float3 surfaceNormal = math.normalizesafe(hit.Position - targetPos);
                    surfaceNormal.y = 0;
                    float normalLen = math.length(surfaceNormal);

                    if (normalLen < 0.001f)
                    {
                        return velocity;
                    }

                    surfaceNormal = surfaceNormal / normalLen;

                    float3 moveDir = math.normalizesafe(velocity);
                    float dot = math.dot(moveDir, surfaceNormal);

                    // cos(45°) ≈ 0.707
                    if (math.abs(dot) > 0.707f)
                    {
                        // 수직 충돌 - 멈춤
                        return float3.zero;
                    }
                    else
                    {
                        // 사선 충돌 - 벽면에 투영 (미끄러짐)
                        float3 slidingVelocity = velocity - math.project(velocity, surfaceNormal);
                        return slidingVelocity;
                    }
                }
            }

            return velocity; // 충돌 없음
        }
    }
}
