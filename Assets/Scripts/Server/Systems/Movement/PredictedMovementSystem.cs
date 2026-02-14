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

            // 이동 그룹 (Waypoint 보유, 비활성 포함 - 공격 중 Separation 유지)
            _movingQuery = state.GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadWrite<LocalTransform>(),
                    ComponentType.ReadWrite<PhysicsVelocity>(),
                    ComponentType.ReadWrite<MovementWaypoints>(),
                    ComponentType.ReadOnly<MovementDynamics>(),
                    ComponentType.ReadOnly<ObstacleRadius>(),
                    ComponentType.ReadOnly<MovementGoal>()
                },
                Options = EntityQueryOptions.IgnoreComponentEnabledState
            });
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
            EnabledRefRW<MovementWaypoints> waypointsEnabled,
            in MovementDynamics dynamics,
            in ObstacleRadius obstacleRadius,
            in MovementGoal goal)
        {
            // 공격 중이거나 waypoints 비활성화 시 이동은 스킵하되 Separation은 유지
            bool isEnemyAttacking = EnemyTagLookup.HasComponent(entity) &&
                                    EnemyStateLookup.TryGetComponent(entity, out EnemyState enemyState) &&
                                    enemyState.CurrentState == EnemyContext.Attacking;

            bool isUnitAttacking = ActionStateLookup.TryGetComponent(entity, out UnitActionState actionState) &&
                                   actionState.State == Action.Attacking;

            bool isAttacking = isEnemyAttacking || isUnitAttacking;
            bool isWaypointsDisabled = !waypointsEnabled.ValueRO;
            bool isPathPending = goal.IsPathDirty;
            bool skipMovement = isAttacking || isWaypointsDisabled || isPathPending;

            float3 currentPos = transform.Position;
            float3 desiredVelocity = float3.zero;

            if (!skipMovement)
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

                float arrivalR = waypoints.ArrivalRadius > 0 ? waypoints.ArrivalRadius : obstacleRadius.Radius + 0.1f;
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

            float3 separationForce = CalculateSeparation(currentPos, obstacleRadius.Radius, entity, iAmEnemy, iAmFlying, iAmGathering, out float3 hardPush);
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

            // Separation 진동 감지: 최종 목적지 근처에서 밀려나는 경우 정지
            // 다수 유닛이 동일 목적지로 이동 시 도착한 유닛의 Separation이
            // 이동 중 유닛을 도착 반경 밖으로 밀어내는 현상 방지
            if (!skipMovement && !waypoints.HasNext)
            {
                float3 tp = waypoints.Current;
                tp.y = currentPos.y;
                float3 toTarget = tp - currentPos;
                float dSq = math.lengthsq(toTarget);
                float aR = waypoints.ArrivalRadius > 0 ? waypoints.ArrivalRadius : obstacleRadius.Radius + 0.1f;
                float expandedR = aR * 2f;

                if (dSq < expandedR * expandedR && math.dot(finalVelocity, toTarget) <= 0)
                {
                    velocity.Linear = float3.zero;
                    velocity.Angular = float3.zero;
                    return;
                }
            }

            // Apply
            transform.Position += finalVelocity * DeltaTime;

            // 이동 후 벽 관통 차단 (안전망)
            if (!iAmFlying)
            {
                ClampToWall(ref transform.Position, obstacleRadius.Radius, in CollisionWorld, WallFilter);
            }

            // Entity 겹침 위치 보정 (Hard Constraint)
            if (math.lengthsq(hardPush) > 0.0001f)
            {
                float maxPush = obstacleRadius.Radius;
                float pushLenSq = math.lengthsq(hardPush);
                if (pushLenSq > maxPush * maxPush)
                    hardPush *= maxPush / math.sqrt(pushLenSq);

                // 벽 방향 성분 사전 제거 (벽 관통 방지)
                if (!iAmFlying)
                    hardPush = RemoveWallComponent(transform.Position, hardPush, obstacleRadius.Radius);

                transform.Position += hardPush;

                // Entity push가 벽 안으로 밀 수 있으므로 벽 관통 재검사
                if (!iAmFlying)
                {
                    ClampToWall(ref transform.Position, obstacleRadius.Radius, in CollisionWorld, WallFilter);
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

        private float3 CalculateSeparation(
            float3 myPos, float myRadius, Entity myEntity,
            bool iAmEnemy, bool iAmFlying, bool iAmGathering,
            out float3 hardPush)
        {
            float3 separation = float3.zero;
            hardPush = float3.zero;

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

                                // 비선형 force: 깊이 침투 시 기하급수적으로 강해짐
                                float overlapRatio = overlap / combinedRadius;
                                float forceMag = overlap * (1.0f + overlapRatio * 3.0f);
                                separation += (toOther / dist) * forceMag;

                                // Hard constraint: 실제 반경(마진 제외) 기준 겹침 위치 보정
                                float hardCombinedR = myRadius + otherRadius;
                                if (dist < hardCombinedR)
                                {
                                    float hardOverlap = hardCombinedR - dist;
                                    hardPush += (toOther / dist) * (hardOverlap * 0.5f);
                                }
                            }

                        } while (SpatialMap.TryGetNextValue(out neighbor, ref it));
                    }
                }
            }
            return separation;
        }

        private float3 RemoveWallComponent(float3 position, float3 push, float radius)
        {
            var pointInput = new PointDistanceInput
            {
                Position = position + new float3(0, 0.5f, 0),
                MaxDistance = radius + 0.1f,
                Filter = WallFilter
            };

            if (CollisionWorld.CalculateDistance(pointInput, out DistanceHit hit))
            {
                float3 wallNormal = math.normalizesafe(hit.SurfaceNormal);
                wallNormal.y = 0;
                if (math.lengthsq(wallNormal) > 0.001f)
                {
                    wallNormal = math.normalize(wallNormal);
                    float dot = math.dot(push, wallNormal);
                    if (dot < 0)
                        push -= wallNormal * dot;
                }
            }

            return push;
        }

        private static void ClampToWall(
            ref float3 position, float radius,
            in CollisionWorld collisionWorld, CollisionFilter wallFilter)
        {
            // 반복 처리: 코너(두 벽 교차)에서 첫 번째 벽으로 밀어낸 후 두 번째 벽 겹침 재검사
            for (int i = 0; i < 3; i++)
            {
                var clampInput = new PointDistanceInput
                {
                    Position = position + new float3(0, 0.5f, 0),
                    MaxDistance = radius,
                    Filter = wallFilter
                };

                if (!collisionWorld.CalculateDistance(clampInput, out DistanceHit clampHit))
                    break;

                float overlap = radius - clampHit.Distance;
                if (overlap <= 0.05f)
                    break;

                float3 pushNormal = math.normalizesafe(clampHit.SurfaceNormal);
                pushNormal.y = 0;
                if (math.lengthsq(pushNormal) <= 0.001f)
                    break;

                pushNormal = math.normalize(pushNormal);
                position += pushNormal * (overlap + 0.02f);
            }
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
