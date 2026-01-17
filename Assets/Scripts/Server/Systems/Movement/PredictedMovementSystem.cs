using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;
using Shared;

namespace Server
{
    /// <summary>
    /// 서버 전용 Kinematic 이동 시스템
    /// - LocalTransform 직접 제어 (Kinematic Body)
    /// - Entity 위치 기반 Separation
    /// - 벽 충돌 검사 (SphereCast)
    /// - 클라이언트는 Ghost 보간으로 부드럽게 표시
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PathfindingSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial struct PredictedMovementSystem : ISystem
    {
        private EntityQuery _movingEntitiesQuery;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PhysicsWorldSingleton>();

            // Separation 대상 쿼리 (위치 + 반경이 있는 모든 이동 가능 엔티티)
            _movingEntitiesQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<ObstacleRadius>()
            );
        }

        public void OnUpdate(ref SystemState state)
        {
            // PhysicsWorld 가져오기 (벽 충돌 검사용)
            var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().PhysicsWorld;
            float deltaTime = SystemAPI.Time.DeltaTime;

            // 모든 이동 가능 엔티티의 위치/반경 수집 (Separation용)
            var entities = _movingEntitiesQuery.ToEntityArray(Allocator.TempJob);
            var positions = new NativeArray<float3>(entities.Length, Allocator.TempJob);
            var radii = new NativeArray<float>(entities.Length, Allocator.TempJob);

            for (int i = 0; i < entities.Length; i++)
            {
                positions[i] = state.EntityManager.GetComponentData<LocalTransform>(entities[i]).Position;
                radii[i] = state.EntityManager.GetComponentData<ObstacleRadius>(entities[i]).Radius;
            }

            // 장애물 레이어 필터 (리소스 노드 + 건물 충돌 체크)
            // Unity Physics CollisionFilter: BelongsTo=자신, CollidesWith=대상
            var structureFilter = new CollisionFilter
            {
                BelongsTo = ~0u,                        // 모든 레이어
                CollidesWith = (1u << 6) | (1u << 7),  // ResourceNode(6) + Structure(7)
                GroupIndex = 0
            };

            var job = new KinematicMovementJob
            {
                DeltaTime = deltaTime,
                AllEntities = entities,
                AllPositions = positions,
                AllRadii = radii,
                SeparationStrength = 5.0f,
                MaxSeparationDistance = 2.0f,
                CollisionWorld = physicsWorld.CollisionWorld,
                StructureFilter = structureFilter
            };

            state.Dependency = job.ScheduleParallel(state.Dependency);

            // Job 완료 후 해제
            entities.Dispose(state.Dependency);
            positions.Dispose(state.Dependency);
            radii.Dispose(state.Dependency);
        }
    }

    [BurstCompile]
    public partial struct KinematicMovementJob : IJobEntity
    {
        public float DeltaTime;
        [ReadOnly] public NativeArray<Entity> AllEntities;
        [ReadOnly] public NativeArray<float3> AllPositions;
        [ReadOnly] public NativeArray<float> AllRadii;
        public float SeparationStrength;
        public float MaxSeparationDistance;

        // 벽 충돌 검사용
        [ReadOnly] public CollisionWorld CollisionWorld;
        public CollisionFilter StructureFilter;

        /// <summary>
        /// Kinematic 이동 처리 (LocalTransform 직접 제어)
        /// </summary>
        private void Execute(
            Entity entity,
            ref LocalTransform transform,
            ref PhysicsVelocity velocity,
            ref MovementWaypoints waypoints,
            in MovementDynamics dynamics,
            in ObstacleRadius obstacleRadius)
        {
            float3 currentPos = transform.Position;

            // ============================================
            // 1. Waypoint 이동 속도 계산
            // ============================================
            float3 targetPos = waypoints.Current;
            targetPos.y = currentPos.y;

            float3 toTarget = targetPos - currentPos;
            float distance = math.length(toTarget);

            // 코너링 (웨이포인트 스위칭)
            const float CornerRadius = 0.5f;
            if (waypoints.HasNext && distance < CornerRadius)
            {
                waypoints.Current = waypoints.Next;
                waypoints.HasNext = false;

                targetPos = waypoints.Current;
                targetPos.y = currentPos.y;
                toTarget = targetPos - currentPos;
                distance = math.length(toTarget);
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
            float currentSpeedInDir = math.max(0f, math.dot(velocity.Linear, moveDir));
            float speedDiff = targetSpeed - currentSpeedInDir;
            float accelToUse = speedDiff > 0f ? dynamics.Acceleration : dynamics.Deceleration;
            float newSpeed = currentSpeedInDir + math.sign(speedDiff) * math.min(math.abs(speedDiff), accelToUse * DeltaTime);
            newSpeed = math.clamp(newSpeed, 0f, dynamics.MaxSpeed);

            float3 moveVelocity = moveDir * newSpeed;
            moveVelocity.y = 0f;

            // ============================================
            // 2. Separation (Entity 위치 기반 - 결정론적)
            // ============================================
            float3 separationVelocity = CalculateSeparation(currentPos, obstacleRadius.Radius, entity);

            // ============================================
            // 3. 희망 속도 (Desired Velocity)
            // ============================================
            float3 desiredVelocity = moveVelocity + separationVelocity * SeparationStrength;
            desiredVelocity.y = 0f;

            // ============================================
            // 4. 벽 충돌 해결 (ResolveWallCollision)
            // ============================================
            float3 finalVelocity = ResolveWallCollision(currentPos, desiredVelocity, obstacleRadius.Radius);

            // ============================================
            // 5. 최종 위치 적용 (Kinematic이므로 직접 설정 필수!)
            // ============================================
            float3 newPos = currentPos + finalVelocity * DeltaTime;
            newPos.y = currentPos.y;
            transform.Position = newPos;

            // ============================================
            // 6. PhysicsVelocity 동기화
            // ============================================
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
        /// Separation 계산 (Entity 위치 기반 - 결정론적)
        /// </summary>
        private float3 CalculateSeparation(float3 myPos, float myRadius, Entity myEntity)
        {
            float3 separationForce = float3.zero;
            float searchRadius = myRadius + MaxSeparationDistance;

            for (int i = 0; i < AllEntities.Length; i++)
            {
                if (AllEntities[i] == myEntity) continue;

                float3 otherPos = AllPositions[i];
                float otherRadius = AllRadii[i];

                float3 toOther = otherPos - myPos;
                toOther.y = 0;
                float dist = math.length(toOther);

                // 검색 범위 밖이면 스킵
                if (dist > searchRadius) continue;

                float combinedRadius = myRadius + otherRadius;

                if (dist < combinedRadius && dist > 0.001f)
                {
                    float overlap = combinedRadius - dist;
                    float3 pushDir = -math.normalize(toOther);
                    float strength = math.saturate(overlap / combinedRadius);
                    separationForce += pushDir * overlap * strength;
                }
            }

            return separationForce;
        }

        /// <summary>
        /// 벽 충돌 해결 - 벽에 부딪히면 미끄러지는 속도 반환
        /// </summary>
        private float3 ResolveWallCollision(float3 currentPos, float3 velocity, float radius)
        {
            float speed = math.length(velocity);
            if (speed < 0.001f) return velocity;

            float3 moveDir = velocity / speed;

            // RayCast 시작점: 유닛 중심에서 반경만큼 이동 방향으로 앞으로
            // (이미 건물 안에 있을 때 내부에서 시작하는 것 방지)
            float3 rayStart = currentPos + new float3(0, 0.5f, 0);

            // 목표 지점까지 체크
            float castDistance = radius + 0.5f;
            float3 rayEnd = rayStart + moveDir * castDistance;

            var rayInput = new RaycastInput
            {
                Start = rayStart,
                End = rayEnd,
                Filter = StructureFilter
            };

            if (CollisionWorld.CastRay(rayInput, out Unity.Physics.RaycastHit hit))
            {
                // 벽까지 거리
                float hitDistance = hit.Fraction * castDistance;

                // 벽이 가까우면 충돌 처리
                if (hitDistance < radius)
                {
                    // 벽면 법선 구하기
                    float3 normal = hit.SurfaceNormal;
                    normal.y = 0;
                    float normalLen = math.length(normal);

                    if (normalLen > 0.001f)
                    {
                        normal = normal / normalLen;

                        // 속도에서 벽 방향 성분 제거 (미끄러짐)
                        float dot = math.dot(velocity, normal);
                        if (dot < 0) // 벽을 향해 가는 경우만
                        {
                            return velocity - dot * normal;
                        }
                    }
                }
            }

            // 충돌 없으면 원래 속도 유지
            return velocity;
        }
    }
}
