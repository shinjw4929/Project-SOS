using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using Unity.Collections.LowLevel.Unsafe;
using Shared;

namespace Server
{
    /// <summary>
    /// 유닛 이동 시스템 (서버 전용)
    /// <para>- SpatialMaps 싱글톤에서 MovementMap 사용</para>
    /// <para>- 충돌 회피 및 벽 충돌 처리</para>
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PathfindingSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial struct PredictedMovementSystem : ISystem
    {
        private EntityQuery _movingQuery;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PhysicsWorldSingleton>();
            state.RequireForUpdate<SpatialMaps>();

            // 이동 그룹 (Waypoint 보유)
            _movingQuery = state.GetEntityQuery(
                ComponentType.ReadWrite<LocalTransform>(),
                ComponentType.ReadWrite<PhysicsVelocity>(),
                ComponentType.ReadWrite<MovementWaypoints>(),
                ComponentType.ReadOnly<MovementDynamics>(),
                ComponentType.ReadOnly<ObstacleRadius>()
            );
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // SpatialMaps 싱글톤에서 MovementMap 가져오기
            if (!SystemAPI.TryGetSingleton<SpatialMaps>(out var spatialMaps) || !spatialMaps.IsValid)
                return;

            var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().PhysicsWorld;
            float dt = SystemAPI.Time.DeltaTime;

            // Lookup 준비
            var enemyTagLookup = SystemAPI.GetComponentLookup<EnemyTag>(true);
            var enemyStateLookup = SystemAPI.GetComponentLookup<EnemyState>(true);
            var intentLookup = SystemAPI.GetComponentLookup<UnitIntentState>(true);
            var actionStateLookup = SystemAPI.GetComponentLookup<UnitActionState>(true);
            var transformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true);
            var radiusLookup = SystemAPI.GetComponentLookup<ObstacleRadius>(true);
            var flyingTagLookup = SystemAPI.GetComponentLookup<FlyingTag>(true);

            var wallFilter = new CollisionFilter
            {
                BelongsTo = ~0u,
                CollidesWith = (1u << 6) | (1u << 7),
                GroupIndex = 0
            };

            var moveJob = new KinematicMovementJob
            {
                DeltaTime = dt,
                SpatialMap = spatialMaps.MovementMap,
                TransformLookup = transformLookup,
                RadiusLookup = radiusLookup,
                EnemyTagLookup = enemyTagLookup,
                EnemyStateLookup = enemyStateLookup,
                IntentLookup = intentLookup,
                ActionStateLookup = actionStateLookup,
                FlyingTagLookup = flyingTagLookup,
                CellSize = SpatialHashUtility.MovementCellSize,
                SeparationStrength = 4.0f,
                CollisionWorld = physicsWorld.CollisionWorld,
                WallFilter = wallFilter
            };

            state.Dependency = moveJob.ScheduleParallel(_movingQuery, state.Dependency);
        }
    }

    [BurstCompile]
    public partial struct KinematicMovementJob : IJobEntity
    {
        public float DeltaTime;

        [ReadOnly] public NativeParallelMultiHashMap<int, SpatialMovementEntry> SpatialMap;

        [ReadOnly]
        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<LocalTransform> TransformLookup;

        [ReadOnly] public ComponentLookup<ObstacleRadius> RadiusLookup;
        [ReadOnly] public ComponentLookup<EnemyTag> EnemyTagLookup;
        [ReadOnly] public ComponentLookup<EnemyState> EnemyStateLookup;
        [ReadOnly] public ComponentLookup<UnitIntentState> IntentLookup;
        [ReadOnly] public ComponentLookup<UnitActionState> ActionStateLookup;
        [ReadOnly] public ComponentLookup<FlyingTag> FlyingTagLookup;

        public float CellSize;
        public float SeparationStrength;

        [ReadOnly] public CollisionWorld CollisionWorld;
        public CollisionFilter WallFilter;

        public void Execute(
            Entity entity,
            ref LocalTransform transform,
            ref PhysicsVelocity velocity,
            ref MovementWaypoints waypoints,
            in MovementDynamics dynamics,
            in ObstacleRadius obstacleRadius)
        {
            // 공격 중이면 이동은 스킵하되 분리는 유지
            bool isEnemyAttacking = EnemyTagLookup.HasComponent(entity) &&
                                    EnemyStateLookup.TryGetComponent(entity, out EnemyState enemyState) &&
                                    enemyState.CurrentState == EnemyContext.Attacking;

            bool isUnitAttacking = ActionStateLookup.TryGetComponent(entity, out UnitActionState actionState) &&
                                   actionState.State == Action.Attacking;

            bool isAttacking = isEnemyAttacking || isUnitAttacking;

            float3 currentPos = transform.Position;
            float3 desiredVelocity = float3.zero;

            if (!isAttacking)
            {
                float3 targetPos = waypoints.Current;
                targetPos.y = currentPos.y;

                // Waypoint Logic
                float3 toTarget = targetPos - currentPos;
                float distSq = math.lengthsq(toTarget);

                if (waypoints.HasNext && distSq < 0.25f)
                {
                    waypoints.Current = waypoints.Next;
                    waypoints.HasNext = false;
                    targetPos = waypoints.Current;
                    targetPos.y = currentPos.y;
                    toTarget = targetPos - currentPos;
                    distSq = math.lengthsq(toTarget);
                }

                float arrivalR = waypoints.ArrivalRadius > 0 ? waypoints.ArrivalRadius : 0.1f;
                if (!waypoints.HasNext && distSq < arrivalR * arrivalR)
                {
                    velocity.Linear = float3.zero;
                    return;
                }

                float dist = math.sqrt(distSq);

                // Velocity Logic
                float3 moveDir = dist > 0.001f ? toTarget / dist : float3.zero;
                float targetSpeed = dynamics.MaxSpeed;

                if (!waypoints.HasNext)
                {
                    float decel = math.max(0.1f, dynamics.Deceleration);
                    float slowingDist = (dynamics.MaxSpeed * dynamics.MaxSpeed) / (2f * decel);
                    if (dist < slowingDist)
                        targetSpeed = math.lerp(0, dynamics.MaxSpeed, dist / slowingDist);
                }

                float currentSpeed = math.length(velocity.Linear);
                float speedDiff = targetSpeed - currentSpeed;
                float accelRate = speedDiff > 0 ? dynamics.Acceleration : dynamics.Deceleration;
                float newSpeed = currentSpeed + math.sign(speedDiff) * math.min(math.abs(speedDiff), accelRate * DeltaTime);

                desiredVelocity = moveDir * newSpeed;
                desiredVelocity.y = 0;
            }

            // Separation (Avoidance) - 공격 중에도 실행
            bool iAmEnemy = EnemyTagLookup.HasComponent(entity);
            bool iAmFlying = FlyingTagLookup.HasComponent(entity);
            bool iAmGathering = false;
            if (IntentLookup.TryGetComponent(entity, out UnitIntentState intent) && intent.State == Intent.Gather)
                iAmGathering = true;

            float3 separationForce = CalculateSeparation(currentPos, obstacleRadius.Radius, entity, iAmEnemy, iAmFlying, iAmGathering);
            float3 finalVelocity = desiredVelocity + (separationForce * SeparationStrength);

            // Cap Velocity
            float maxLimit = dynamics.MaxSpeed * 1.5f;
            if (math.lengthsq(finalVelocity) > maxLimit * maxLimit)
            {
                finalVelocity = math.normalizesafe(finalVelocity) * maxLimit;
            }

            // Wall Collision (flying 엔티티는 벽 무시)
            if (!iAmFlying)
            {
                finalVelocity = ResolveWallCollision(currentPos, finalVelocity, obstacleRadius.Radius, DeltaTime, iAmEnemy);
            }

            // Apply
            transform.Position += finalVelocity * DeltaTime;

            // 위치 보정: Separation Force로 인한 벽 관통 차단 (안전망)
            if (!iAmFlying)
            {
                var clampInput = new PointDistanceInput
                {
                    Position = transform.Position + new float3(0, 0.5f, 0),
                    MaxDistance = obstacleRadius.Radius,
                    Filter = WallFilter
                };

                if (CollisionWorld.CalculateDistance(clampInput, out DistanceHit clampHit))
                {
                    float overlap = obstacleRadius.Radius - clampHit.Distance;
                    if (overlap > 0.05f)
                    {
                        float3 pushNormal = math.normalizesafe(clampHit.SurfaceNormal);
                        pushNormal.y = 0;
                        if (math.lengthsq(pushNormal) > 0.001f)
                        {
                            pushNormal = math.normalize(pushNormal);
                            transform.Position += pushNormal * (overlap + 0.02f);
                        }
                    }
                }
            }

            velocity.Linear = finalVelocity;
            velocity.Angular = float3.zero;

            if (math.lengthsq(finalVelocity) > 0.01f)
            {
                quaternion targetRot = quaternion.LookRotationSafe(math.normalizesafe(finalVelocity), math.up());
                transform.Rotation = math.slerp(transform.Rotation, targetRot, dynamics.RotationSpeed * DeltaTime);
            }
        }

        private float3 CalculateSeparation(float3 myPos, float myRadius, Entity myEntity, bool iAmEnemy, bool iAmFlying, bool iAmGathering)
        {
            float3 separation = float3.zero;

            for (int x = -1; x <= 1; x++)
            {
                for (int z = -1; z <= 1; z++)
                {
                    int hash = SpatialHashUtility.GetCellHash(myPos, x, z, CellSize);

                    if (SpatialMap.TryGetFirstValue(hash, out SpatialMovementEntry neighbor, out var it))
                    {
                        do
                        {
                            if (neighbor.Entity == myEntity) continue;

                            // Flying <-> Ground 충돌 스킵
                            bool neighborIsFlying = FlyingTagLookup.HasComponent(neighbor.Entity);
                            if (iAmFlying != neighborIsFlying) continue;

                            // Lookup을 통해 이웃 데이터 조회
                            bool isEnemy = EnemyTagLookup.HasComponent(neighbor.Entity);
                            bool isGathering = false;
                            if (IntentLookup.TryGetComponent(neighbor.Entity, out UnitIntentState nIntent) && nIntent.State == Intent.Gather)
                                isGathering = true;

                            bool shouldCollide = iAmEnemy || isEnemy || (!iAmGathering && !isGathering);
                            if (!shouldCollide) continue;

                            if (!TransformLookup.TryGetComponent(neighbor.Entity, out LocalTransform neighborTransform))
                                continue;
                            if (!RadiusLookup.TryGetComponent(neighbor.Entity, out ObstacleRadius neighborRadius))
                                continue;

                            float3 otherPos = neighborTransform.Position;
                            float otherRadius = neighborRadius.Radius;

                            float3 toOther = myPos - otherPos;
                            toOther.y = 0;

                            float distSq = math.lengthsq(toOther);
                            float combinedRadius = myRadius + otherRadius + 0.3f;

                            if (distSq < combinedRadius * combinedRadius && distSq > 0.0001f)
                            {
                                float dist = math.sqrt(distSq);
                                float overlap = combinedRadius - dist;
                                separation += (toOther / dist) * overlap;
                            }

                        } while (SpatialMap.TryGetNextValue(out neighbor, ref it));
                    }
                }
            }
            return separation;
        }

        private float3 ResolveWallCollision(float3 currentPos, float3 velocity, float radius, float dt, bool isEnemy)
        {
            float moveSpeed = math.length(velocity);
            if (moveSpeed < 0.001f) return velocity;

            float3 moveDir = velocity / moveSpeed;
            float moveDist = moveSpeed * dt;

            // [1] 이동 방향 Raycast
            var rayInput = new RaycastInput
            {
                Start = currentPos + new float3(0, 0.5f, 0),
                End = currentPos + new float3(0, 0.5f, 0) + (moveDir * (moveDist + radius + 0.1f)),
                Filter = WallFilter
            };

            if (CollisionWorld.CastRay(rayInput, out RaycastHit hit))
            {
                float distToHit = hit.Fraction * math.length(rayInput.End - rayInput.Start);

                if (distToHit < radius + 0.05f)
                {
                    float3 normal = hit.SurfaceNormal;
                    normal.y = 0;

                    if (math.lengthsq(normal) > 0.001f)
                    {
                        normal = math.normalize(normal);

                        float dot = math.dot(velocity, normal);
                        if (dot < 0)
                        {
                            if (!isEnemy)
                            {
                                // 유닛: 속도 절대값 유지
                                float originalSpeed = math.length(velocity);
                                velocity = velocity - (normal * dot);
                                float newSpeed = math.length(velocity);
                                if (newSpeed > 0.001f)
                                {
                                    velocity = (velocity / newSpeed) * originalSpeed;
                                }
                            }
                            else
                            {
                                // 적: 기존 로직
                                velocity = velocity - (normal * dot);
                            }
                        }
                    }
                }
            }

            // [2] 주변 전방향 충돌 검사
            var pointInput = new PointDistanceInput
            {
                Position = currentPos + new float3(0, 0.5f, 0),
                MaxDistance = radius + 0.1f,
                Filter = WallFilter
            };

            if (CollisionWorld.CalculateDistance(pointInput, out DistanceHit distHit))
            {
                float3 wallNormal = math.normalizesafe(distHit.SurfaceNormal);
                wallNormal.y = 0;

                if (math.lengthsq(wallNormal) > 0.001f)
                {
                    wallNormal = math.normalize(wallNormal);
                    float overlap = radius - distHit.Distance;

                    if (overlap > 0)
                    {
                        float dotToWall = math.dot(velocity, wallNormal);
                        if (dotToWall < 0)
                        {
                            if (!isEnemy)
                            {
                                // 유닛: 속도 절대값 유지
                                float originalSpeed = math.length(velocity);
                                velocity -= wallNormal * dotToWall;
                                float newSpeed = math.length(velocity);
                                if (newSpeed > 0.001f)
                                {
                                    velocity = (velocity / newSpeed) * originalSpeed;
                                }
                            }
                            else
                            {
                                // 적: 기존 로직
                                velocity -= wallNormal * dotToWall;
                            }
                        }

                        float pushSpeed = math.min(overlap * 10f, 5f);
                        velocity += wallNormal * pushSpeed;
                    }
                }
            }

            return velocity;
        }
    }
}
