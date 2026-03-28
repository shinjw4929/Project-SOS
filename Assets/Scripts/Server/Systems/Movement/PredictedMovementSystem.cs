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
    /// <para>- 충돌 회피 및 그리드 기반 벽 충돌 처리</para>
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(FlowFieldSteeringSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [BurstCompile]
    public partial struct PredictedMovementSystem : ISystem
    {
        private EntityQuery _movingQuery;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GridSettings>();
            state.RequireForUpdate<SpatialMaps>();

            // 이동 그룹 (Waypoint 보유, 비활성 포함)
            _movingQuery = state.GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadWrite<LocalTransform>(),
                    ComponentType.ReadWrite<PhysicsVelocity>(),
                    ComponentType.ReadWrite<MovementWaypoints>(),
                    ComponentType.ReadWrite<CachedAvoidanceDir>(),
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

            float dt = SystemAPI.Time.DeltaTime;

            // Grid 데이터 준비
            var gridSettings = SystemAPI.GetSingleton<GridSettings>();
            var gridEntity = SystemAPI.GetSingletonEntity<GridSettings>();
            var gridCells = SystemAPI.GetBuffer<GridCell>(gridEntity).AsNativeArray();

            // Lookup 준비
            var enemyTagLookup = SystemAPI.GetComponentLookup<EnemyTag>(true);
            var enemyStateLookup = SystemAPI.GetComponentLookup<EnemyState>(true);
            var intentLookup = SystemAPI.GetComponentLookup<UnitIntentState>(true);
            var actionStateLookup = SystemAPI.GetComponentLookup<UnitActionState>(true);
            var transformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true);
            var radiusLookup = SystemAPI.GetComponentLookup<ObstacleRadius>(true);
            var flyingTagLookup = SystemAPI.GetComponentLookup<FlyingTag>(true);

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
                AvoidanceStrength = SystemAPI.TryGetSingleton<GameSettings>(out var movGS) ? movGS.AvoidanceStrength : 2.0f,
                AvoidancePadding = movGS.AvoidancePadding > 0 ? movGS.AvoidancePadding : 0.3f,
                GridCells = gridCells,
                GridSettings = gridSettings,
                FrameCount = (uint)state.GlobalSystemVersion,
                SteeringSliceDivisor = movGS.SteeringSliceDivisor >= 1 ? movGS.SteeringSliceDivisor : 4u
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
        public float AvoidanceStrength;
        public float AvoidancePadding;
        public uint FrameCount;
        public uint SteeringSliceDivisor;

        [ReadOnly] public NativeArray<GridCell> GridCells;
        public GridSettings GridSettings;

        public void Execute(
            Entity entity,
            ref LocalTransform transform,
            ref PhysicsVelocity velocity,
            ref MovementWaypoints waypoints,
            ref CachedAvoidanceDir cachedAvoidance,
            EnabledRefRW<MovementWaypoints> waypointsEnabled,
            in MovementDynamics dynamics,
            in ObstacleRadius obstacleRadius,
            in MovementGoal goal)
        {
            // 공격 중이거나 waypoints 비활성화 시 이동 스킵 (정지 유닛은 제자리 유지)
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

            // Steering 회피 (밀림 없이 방향만 조정)
            bool iAmEnemy = EnemyTagLookup.HasComponent(entity);
            bool iAmFlying = FlyingTagLookup.HasComponent(entity);
            bool iAmWorking = false;
            if (IntentLookup.TryGetComponent(entity, out UnitIntentState intent)
                && (intent.State == Intent.Gather || intent.State == Intent.Build))
                iAmWorking = true;

            float3 finalVelocity;
            if (skipMovement)
            {
                finalVelocity = float3.zero;
            }
            else
            {
                bool isMySteeringFrame = (SteeringSliceDivisor <= 1)
                    || ((uint)entity.Index % SteeringSliceDivisor) == (FrameCount % SteeringSliceDivisor);

                if (isMySteeringFrame || cachedAvoidance.Strength < 0.001f)
                {
                    finalVelocity = CalculateSteeringAvoidance(currentPos, obstacleRadius.Radius,
                        desiredVelocity, entity, iAmEnemy, iAmFlying, iAmWorking);

                    // 캐시 갱신
                    float desiredSpeed = math.length(desiredVelocity);
                    if (desiredSpeed > 0.001f)
                    {
                        float3 desiredDir = desiredVelocity / desiredSpeed;
                        float3 actualDir = math.normalizesafe(finalVelocity);
                        float deviation = 1.0f - math.dot(desiredDir, actualDir);
                        cachedAvoidance.Direction = actualDir;
                        cachedAvoidance.Strength = math.saturate(deviation * 2.0f);
                    }
                    else
                    {
                        cachedAvoidance.Direction = float3.zero;
                        cachedAvoidance.Strength = 0f;
                    }
                }
                else
                {
                    // 캐시된 방향 재사용
                    float desiredSpeed = math.length(desiredVelocity);
                    if (cachedAvoidance.Strength > 0.001f && desiredSpeed > 0.001f)
                    {
                        float3 desiredDir = math.normalizesafe(desiredVelocity);
                        float3 adjustedDir = math.normalizesafe(
                            math.lerp(desiredDir, cachedAvoidance.Direction, cachedAvoidance.Strength));
                        finalVelocity = adjustedDir * desiredSpeed;
                    }
                    else
                    {
                        finalVelocity = desiredVelocity;
                    }
                }
            }

            // Cap Velocity
            float maxLimit = dynamics.MaxSpeed * 1.5f;
            if (math.lengthsq(finalVelocity) > maxLimit * maxLimit)
            {
                finalVelocity = math.normalizesafe(finalVelocity) * maxLimit;
            }

            // Wall Collision — 그리드 기반 (flying 엔티티는 벽 무시)
            if (!iAmFlying)
            {
                finalVelocity = ResolveWallCollision(currentPos, finalVelocity, obstacleRadius.Radius, DeltaTime, iAmEnemy);
            }

            // Apply — 이동 전 벽 검증
            float3 newPos = currentPos + finalVelocity * DeltaTime;
            if (!iAmFlying && IsOverlappingBlockedCell(newPos, obstacleRadius.Radius))
            {
                // 축별 분리 시도
                float3 xOnly = currentPos + new float3(finalVelocity.x * DeltaTime, 0, 0);
                float3 zOnly = currentPos + new float3(0, 0, finalVelocity.z * DeltaTime);
                if (!IsOverlappingBlockedCell(xOnly, obstacleRadius.Radius))
                    newPos = xOnly;
                else if (!IsOverlappingBlockedCell(zOnly, obstacleRadius.Radius))
                    newPos = zOnly;
                else
                    newPos = currentPos; // 둘 다 blocked → 정지
            }
            transform.Position = newPos;

            // 이동 후 벽 관통 차단 (안전망, 5회 반복)
            if (!iAmFlying)
            {
                ClampToWall(ref transform.Position, obstacleRadius.Radius);
            }

            velocity.Linear = finalVelocity;
            velocity.Angular = float3.zero;

            if (math.lengthsq(finalVelocity) > 0.01f)
            {
                quaternion targetRot = quaternion.LookRotationSafe(math.normalizesafe(finalVelocity), math.up());
                transform.Rotation = math.slerp(transform.Rotation, targetRot, dynamics.RotationSpeed * DeltaTime);
            }
        }

        /// <summary>
        /// Steering 기반 회피: 이동 방향만 조정, 위치 직접 변경 없음 (밀림 없음).
        /// 이웃과 겹칠 때 방향을 편향하여 우회. entityIndex 기반 결정론적 좌/우 분산.
        /// </summary>
        private float3 CalculateSteeringAvoidance(
            float3 myPos, float myRadius, float3 desiredVelocity,
            Entity myEntity, bool iAmEnemy, bool iAmFlying, bool iAmWorking)
        {
            if (math.lengthsq(desiredVelocity) < 0.001f) return desiredVelocity;

            float3 adjustedDir = math.normalizesafe(desiredVelocity);
            float desiredSpeed = math.length(desiredVelocity);
            float avoidWeight = 0f;
            float3 avoidDir = float3.zero;

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

                            bool neighborIsFlying = FlyingTagLookup.HasComponent(neighbor.Entity);
                            if (iAmFlying != neighborIsFlying) continue;

                            bool isEnemy = EnemyTagLookup.HasComponent(neighbor.Entity);
                            bool isWorking = false;
                            if (IntentLookup.TryGetComponent(neighbor.Entity, out UnitIntentState nIntent)
                                && (nIntent.State == Intent.Gather || nIntent.State == Intent.Build))
                                isWorking = true;

                            bool shouldAvoid = iAmEnemy || isEnemy || (!iAmWorking && !isWorking);
                            if (!shouldAvoid) continue;

                            if (!TransformLookup.TryGetComponent(neighbor.Entity, out LocalTransform neighborTransform))
                                continue;
                            if (!RadiusLookup.TryGetComponent(neighbor.Entity, out ObstacleRadius neighborRadius))
                                continue;

                            float3 toOther = neighborTransform.Position - myPos;
                            toOther.y = 0;

                            float distSq = math.lengthsq(toOther);
                            float combinedRadius = myRadius + neighborRadius.Radius + AvoidancePadding;

                            if (distSq >= combinedRadius * combinedRadius || distSq < 0.0001f) continue;

                            float dist = math.sqrt(distSq);
                            float3 awayDir = math.normalizesafe(myPos - neighborTransform.Position);
                            awayDir.y = 0;

                            // 결정론적 좌/우 편향 (entityIndex 비교)
                            float3 perpDir = math.cross(awayDir, math.up());
                            if (myEntity.Index < neighbor.Entity.Index)
                                perpDir = -perpDir;

                            float overlap = 1.0f - (dist / combinedRadius);
                            avoidDir += math.normalizesafe(awayDir + perpDir) * overlap;
                            avoidWeight += overlap;

                        } while (SpatialMap.TryGetNextValue(out neighbor, ref it));
                    }
                }
            }

            if (avoidWeight > 0.001f)
            {
                avoidDir = math.normalizesafe(avoidDir);
                float blendFactor = math.saturate(avoidWeight * AvoidanceStrength);
                adjustedDir = math.normalizesafe(math.lerp(adjustedDir, avoidDir, blendFactor));
            }

            return adjustedDir * desiredSpeed;
        }

        /// <summary>
        /// 그리드 기반 벽 충돌: 각 축 독립 검사로 벽 미끄러짐 구현.
        /// X/Z 방향 이동이 path-blocked 셀과 겹치면 해당 축 속도를 제거.
        /// 유닛은 속력 보존, 적은 벡터 삭제.
        /// </summary>
        private float3 ResolveWallCollision(float3 currentPos, float3 velocity, float radius, float dt, bool isEnemy)
        {
            float moveSpeed = math.length(velocity);
            if (moveSpeed < 0.001f) return velocity;

            // X축 이동만 적용한 위치에서 겹침 검사
            float3 testPosX = currentPos + new float3(velocity.x * dt, 0, 0);
            bool blockedX = IsOverlappingBlockedCell(testPosX, radius);

            // Z축 이동만 적용한 위치에서 겹침 검사
            float3 testPosZ = currentPos + new float3(0, 0, velocity.z * dt);
            bool blockedZ = IsOverlappingBlockedCell(testPosZ, radius);

            if (blockedX) velocity.x = 0;
            if (blockedZ) velocity.z = 0;

            // 유닛: 속력 보존 (벽을 따라 미끄러질 때 감속하지 않음)
            if ((blockedX || blockedZ) && !isEnemy)
            {
                float newSpeed = math.length(velocity);
                if (newSpeed > 0.001f)
                    velocity = (velocity / newSpeed) * moveSpeed;
            }

            return velocity;
        }

        /// <summary>
        /// 이동 후 벽 관통 보정 (안전망).
        /// 유닛 AABB가 path-blocked 셀과 겹치면 최소 침투 축 방향으로 밀어냄.
        /// 코너(두 벽 교차)를 위해 5회 반복.
        /// </summary>
        private void ClampToWall(ref float3 position, float radius)
        {
            float cellSize = GridSettings.CellSize;
            float2 origin = GridSettings.GridOrigin;
            int2 gridSize = GridSettings.GridSize;

            for (int iter = 0; iter < 5; iter++)
            {
                float unitMinX = position.x - radius;
                float unitMaxX = position.x + radius;
                float unitMinZ = position.z - radius;
                float unitMaxZ = position.z + radius;

                int cMinX = math.clamp((int)math.floor((unitMinX - origin.x) / cellSize), 0, gridSize.x - 1);
                int cMaxX = math.clamp((int)math.floor((unitMaxX - origin.x) / cellSize), 0, gridSize.x - 1);
                int cMinZ = math.clamp((int)math.floor((unitMinZ - origin.y) / cellSize), 0, gridSize.y - 1);
                int cMaxZ = math.clamp((int)math.floor((unitMaxZ - origin.y) / cellSize), 0, gridSize.y - 1);

                float smallestOverlap = float.MaxValue;
                float3 pushVec = float3.zero;

                for (int cz = cMinZ; cz <= cMaxZ; cz++)
                {
                    for (int cx = cMinX; cx <= cMaxX; cx++)
                    {
                        int idx = cz * gridSize.x + cx;
                        if (GridCells[idx].IsPathBlocked == 0) continue;

                        float cwMinX = cx * cellSize + origin.x;
                        float cwMaxX = cwMinX + cellSize;
                        float cwMinZ = cz * cellSize + origin.y;
                        float cwMaxZ = cwMinZ + cellSize;

                        // AABB 겹침 검사
                        if (unitMaxX <= cwMinX || unitMinX >= cwMaxX ||
                            unitMaxZ <= cwMinZ || unitMinZ >= cwMaxZ)
                            continue;

                        float oL = unitMaxX - cwMinX;
                        float oR = cwMaxX - unitMinX;
                        float oD = unitMaxZ - cwMinZ;
                        float oU = cwMaxZ - unitMinZ;

                        float minOX = math.min(oL, oR);
                        float minOZ = math.min(oD, oU);
                        float minO = math.min(minOX, minOZ);

                        if (minO < smallestOverlap)
                        {
                            smallestOverlap = minO;
                            if (minOX < minOZ)
                            {
                                float dir = oL < oR ? -1f : 1f;
                                pushVec = new float3(dir * (minOX + 0.02f), 0, 0);
                            }
                            else
                            {
                                float dir = oD < oU ? -1f : 1f;
                                pushVec = new float3(0, 0, dir * (minOZ + 0.02f));
                            }
                        }
                    }
                }

                if (smallestOverlap >= float.MaxValue) break;
                position += pushVec;
            }
        }

        private bool IsOverlappingBlockedCell(float3 pos, float radius)
        {
            float cellSize = GridSettings.CellSize;
            float2 origin = GridSettings.GridOrigin;
            int2 gridSize = GridSettings.GridSize;

            int cMinX = math.clamp((int)math.floor((pos.x - radius - origin.x) / cellSize), 0, gridSize.x - 1);
            int cMaxX = math.clamp((int)math.floor((pos.x + radius - origin.x) / cellSize), 0, gridSize.x - 1);
            int cMinZ = math.clamp((int)math.floor((pos.z - radius - origin.y) / cellSize), 0, gridSize.y - 1);
            int cMaxZ = math.clamp((int)math.floor((pos.z + radius - origin.y) / cellSize), 0, gridSize.y - 1);

            for (int cz = cMinZ; cz <= cMaxZ; cz++)
            {
                for (int cx = cMinX; cx <= cMaxX; cx++)
                {
                    int idx = cz * gridSize.x + cx;
                    if (GridCells[idx].IsPathBlocked == 0) continue;

                    float cwMinX = cx * cellSize + origin.x;
                    float cwMaxX = cwMinX + cellSize;
                    float cwMinZ = cz * cellSize + origin.y;
                    float cwMaxZ = cwMinZ + cellSize;

                    if (pos.x + radius > cwMinX && pos.x - radius < cwMaxX &&
                        pos.z + radius > cwMinZ && pos.z - radius < cwMaxZ)
                        return true;
                }
            }
            return false;
        }
    }
}
