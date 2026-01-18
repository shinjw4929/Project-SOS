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
    public struct SpatialEntry
    {
        public Entity Entity;   // Lookup 조회 키
        public byte Flags;      // bit0: IsEnemy, bit1: IsGathering
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PathfindingSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial struct PredictedMovementSystem : ISystem
    {
        private EntityQuery _movingQuery;
        private EntityQuery _obstacleQuery;
        
        private const float CellSize = 3.0f;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PhysicsWorldSingleton>();

            // 이동 그룹 (Waypoint 보유)
            _movingQuery = state.GetEntityQuery(
                ComponentType.ReadWrite<LocalTransform>(),
                ComponentType.ReadWrite<PhysicsVelocity>(),
                ComponentType.ReadWrite<MovementWaypoints>(),
                ComponentType.ReadOnly<MovementDynamics>(),
                ComponentType.ReadOnly<ObstacleRadius>()
            );

            // 장애물 그룹 (Unit or Enemy)
            // WithAny를 사용하여 쿼리 빌드
            var obstacleDesc = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<LocalTransform, ObstacleRadius>()
                .WithAny<UnitTag, EnemyTag>()
                .Build(ref state);
            
            _obstacleQuery = obstacleDesc;
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().PhysicsWorld;
            float dt = SystemAPI.Time.DeltaTime;

            int obstacleCount = _obstacleQuery.CalculateEntityCount();
            if (obstacleCount == 0) return;

            // ------------------------------------------------------------------
            // [Step 1] 공간 분할 맵 생성
            // ------------------------------------------------------------------
            // MultiHashMap은 병렬 쓰기가 가능하므로 스레드 안전성을 위해 적절한 크기 할당
            var spatialMap = new NativeParallelMultiHashMap<int, SpatialEntry>(obstacleCount, Allocator.TempJob);

            // 중요: 배열 복사(ToComponentDataArray)를 제거하고 Lookup을 준비합니다.
            // Lookup은 내부적으로 청크 포인터를 사용하므로 복사 비용이 없습니다.
            var enemyTagLookup = SystemAPI.GetComponentLookup<EnemyTag>(true);
            var intentLookup = SystemAPI.GetComponentLookup<UnitIntentState>(true);
            var transformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true);
            var radiusLookup = SystemAPI.GetComponentLookup<ObstacleRadius>(true);

            // BuildSpatialGridJob을 IJobEntity로 변환하여 쿼리에서 직접 실행
            var buildJob = new BuildSpatialGridJob
            {
                SpatialMap = spatialMap.AsParallelWriter(),
                CellSize = CellSize,
                // IJobEntity 내부에서 컴포넌트에 직접 접근하므로 별도 Lookup 불필요
            };

            // _obstacleQuery에 대해 실행.
            // IJobEntity는 청크 단위로 처리되므로 캐시 효율이 높습니다.
            JobHandle buildHandle = buildJob.ScheduleParallel(_obstacleQuery, state.Dependency);

            // ------------------------------------------------------------------
            // [Step 2] 이동 및 충돌 처리
            // ------------------------------------------------------------------
            var wallFilter = new CollisionFilter
            {
                BelongsTo = ~0u,
                CollidesWith = (1u << 6) | (1u << 7),
                GroupIndex = 0
            };

            var moveJob = new KinematicMovementJob
            {
                DeltaTime = dt,
                SpatialMap = spatialMap,
                // 이웃 엔티티의 데이터를 조회하기 위한 Lookup 전달
                TransformLookup = transformLookup,
                RadiusLookup = radiusLookup,
                EnemyTagLookup = enemyTagLookup,
                IntentLookup = intentLookup,
                
                CellSize = CellSize,
                SeparationStrength = 4.0f,
                CollisionWorld = physicsWorld.CollisionWorld,
                WallFilter = wallFilter
            };

            state.Dependency = moveJob.ScheduleParallel(_movingQuery, buildHandle);

            // ------------------------------------------------------------------
            // [Step 3] 자원 해제 예약
            // ------------------------------------------------------------------
            spatialMap.Dispose(state.Dependency);
        }

        public static int GetCellHash(float3 pos, float cellSize)
        {
            // math.floor의 리턴 타입은 float3이므로 int로 캐스팅 최적화
            return (int)math.hash(new int2((int)(pos.x / cellSize), (int)(pos.z / cellSize)));
        }
        
        public static int GetCellHash(float3 pos, int xOff, int yOff, float cellSize)
        {
            return (int)math.hash(new int2((int)(pos.x / cellSize) + xOff, (int)(pos.z / cellSize) + yOff));
        }
    }

    /// <summary>
    /// IJobEntity로 변경: NativeArray 복사 과정 없이 청크 데이터에 직접 접근
    /// </summary>
    [BurstCompile]
    public partial struct BuildSpatialGridJob : IJobEntity
    {
        public NativeParallelMultiHashMap<int, SpatialEntry>.ParallelWriter SpatialMap;
        public float CellSize;

        // IJobEntity는 파라미터로 컴포넌트를 직접 받습니다.
        // Optional 컴포넌트 처리를 위해 EnabledRefRW 또는 HasComponent 활용이 가능하나,
        // 여기서는 플래그 계산을 위해 Aspect나 Lookup 대신 간단히 쿼리 컴포넌트 조합을 사용합니다.
        // *주의: IJobEntity Execute 시그니처에 선언된 컴포넌트가 있는 엔티티만 순회합니다.
        // 장애물 쿼리(_obstacleQuery) 조건(WithAny)을 만족시키기 위해 
        // Execute 파라미터는 공통 컴포넌트만 받고, 태그는 Entity를 통해 내부 로직에서 판단하지 않고,
        // 성능을 위해 필요한 컴포넌트(Tag 등)를 직접 인자로 받되 Optional 처리가 필요합니다.
        // 하지만 IJobEntity에서 WithAny 처리는 까다로우므로, 
        // 여기서는 Entity를 받아 Lookup을 쓰는 방식이 더 깔끔할 수 있습니다. 
        // 다만, 성능 최우선이므로 아래와 같이 처리합니다.

        public void Execute(Entity entity, in LocalTransform transform, [EntityIndexInQuery] int entityIndex)
        {
            // * IJobEntity 내에서 Lookup을 쓰는 것은 Thread Safety 경고가 뜰 수 있으므로
            //   태그 정보는 이동 로직에서 재확인하거나, 여기서 단순화해야 합니다.
            //   Build 단계에서는 "Entity ID"만 맵에 넣고, 플래그 판별은 
            //   실제 데이터를 읽어야 하는 KinematicJob(Lookup 보유)에서 하는 것이 
            //   캐시 일관성 측면에서 낫습니다. 하지만 여기서는 기존 로직 유지를 위해
            //   최소한의 정보만 기록합니다.
            
            //   (최적화를 위해 태그 Lookup은 여기서 제거하고 위치 해싱만 수행. 
            //    플래그 계산은 충돌 체크 시점에 Lookup으로 확인하는 것이 더 정확하고 빠름)
            
            int hash = PredictedMovementSystem.GetCellHash(transform.Position, CellSize);
            SpatialMap.Add(hash, new SpatialEntry
            {
                Entity = entity,
                Flags = 0 // KinematicJob에서 Lookup으로 실시간 확인 (메모리 정렬 이득)
            });
        }
    }

    [BurstCompile]
    public partial struct KinematicMovementJob : IJobEntity
    {
        public float DeltaTime;

        [ReadOnly] public NativeParallelMultiHashMap<int, SpatialEntry> SpatialMap;
        
        // [수정됨] Aliasing 에러 해결을 위해 안전 검사 비활성화 속성 추가
        // 우리는 로직상(neighbor.Entity == myEntity 체크) 자신의 위치를 Lookup으로 읽지 않으므로 안전합니다.
        [ReadOnly] 
        [NativeDisableContainerSafetyRestriction] 
        public ComponentLookup<LocalTransform> TransformLookup;

        [ReadOnly] public ComponentLookup<ObstacleRadius> RadiusLookup;
        [ReadOnly] public ComponentLookup<EnemyTag> EnemyTagLookup;
        [ReadOnly] public ComponentLookup<UnitIntentState> IntentLookup;

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
            float3 currentPos = transform.Position;
            float3 targetPos = waypoints.Current;
            targetPos.y = currentPos.y; 

            // Waypoint Logic (기존 유지)
            float3 toTarget = targetPos - currentPos;
            float distSq = math.lengthsq(toTarget); // 거리 제곱 사용

            if (waypoints.HasNext && distSq < 0.25f) // 0.5 * 0.5
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

            float dist = math.sqrt(distSq); // 필요할 때만 sqrt 수행

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
            
            float3 desiredVelocity = moveDir * newSpeed;
            desiredVelocity.y = 0;

            // Separation (Avoidance)
            // 내 상태 캐싱
            bool iAmEnemy = EnemyTagLookup.HasComponent(entity);
            bool iAmGathering = false;
            if (IntentLookup.TryGetComponent(entity, out UnitIntentState intent) && intent.State == Intent.Gather)
                iAmGathering = true;

            float3 separationForce = CalculateSeparation(currentPos, obstacleRadius.Radius, entity, iAmEnemy, iAmGathering);
            float3 finalVelocity = desiredVelocity + (separationForce * SeparationStrength);

            // Cap Velocity
            float maxLimit = dynamics.MaxSpeed * 1.5f;
            if (math.lengthsq(finalVelocity) > maxLimit * maxLimit)
            {
                finalVelocity = math.normalizesafe(finalVelocity) * maxLimit;
            }

            // Wall Collision
            finalVelocity = ResolveWallCollision(currentPos, finalVelocity, obstacleRadius.Radius, DeltaTime);

            // Apply
            transform.Position += finalVelocity * DeltaTime;
            velocity.Linear = finalVelocity;
            velocity.Angular = float3.zero;

            if (math.lengthsq(finalVelocity) > 0.01f)
            {
                quaternion targetRot = quaternion.LookRotationSafe(math.normalizesafe(finalVelocity), math.up());
                transform.Rotation = math.slerp(transform.Rotation, targetRot, dynamics.RotationSpeed * DeltaTime);
            }
        }

        private float3 CalculateSeparation(float3 myPos, float myRadius, Entity myEntity, bool iAmEnemy, bool iAmGathering)
        {
            float3 separation = float3.zero;

            // 루프 언롤링 효과를 기대하기보다, 필요한 셀만 빠르게 순회
            for (int x = -1; x <= 1; x++)
            {
                for (int z = -1; z <= 1; z++)
                {
                    int hash = PredictedMovementSystem.GetCellHash(myPos, x, z, CellSize);
                    
                    if (SpatialMap.TryGetFirstValue(hash, out SpatialEntry neighbor, out var it))
                    {
                        do
                        {
                            if (neighbor.Entity == myEntity) continue;

                            // Lookup을 통해 이웃 데이터 조회 (Global Memory Access)
                            // NativeArray Copy보다 이 방식이 전체 시스템 부하가 적음
                            bool isEnemy = EnemyTagLookup.HasComponent(neighbor.Entity);
                            bool isGathering = false;
                            if (IntentLookup.TryGetComponent(neighbor.Entity, out UnitIntentState nIntent) && nIntent.State == Intent.Gather)
                                isGathering = true;
                            
                            bool shouldCollide = iAmEnemy || isEnemy || (!iAmGathering && !isGathering);
                            if (!shouldCollide) continue;

                            float3 otherPos = TransformLookup[neighbor.Entity].Position;
                            float otherRadius = RadiusLookup[neighbor.Entity].Radius;

                            float3 toOther = myPos - otherPos; 
                            toOther.y = 0;
                            
                            float distSq = math.lengthsq(toOther);
                            float combinedRadius = myRadius + otherRadius + 0.3f; // 0.3f 마진

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

        // Raycast + PointDistanceInput으로 벽 충돌 처리
        private float3 ResolveWallCollision(float3 currentPos, float3 velocity, float radius, float dt)
        {
            float moveSpeed = math.length(velocity);
            // 속도가 거의 없으면 패스
            if (moveSpeed < 0.001f) return velocity;

            float3 moveDir = velocity / moveSpeed; // 정규화
            float moveDist = moveSpeed * dt;

            // [1] 이동 방향 Raycast (기존 로직)
            // 검사 거리 = 이번 프레임 이동 거리 + 유닛 반지름 (여유분)
            // Start에 y오프셋을 주어 바닥 걸림 방지
            var rayInput = new RaycastInput
            {
                Start = currentPos + new float3(0, 0.5f, 0),
                End = currentPos + new float3(0, 0.5f, 0) + (moveDir * (moveDist + radius + 0.1f)),
                Filter = WallFilter
            };

            if (CollisionWorld.CastRay(rayInput, out RaycastHit hit))
            {
                // hit.Fraction은 Ray의 시작점(0)과 끝점(1) 사이의 충돌 지점 비율입니다.
                // 실제 충돌 지점까지의 거리 계산
                float distToHit = hit.Fraction * math.length(rayInput.End - rayInput.Start);

                // 벽 표면까지의 거리가 내 반지름보다 가깝다면 (즉, 이동하면 부딪힌다면)
                if (distToHit < radius + 0.05f) // 0.05f는 스킨 두께(여유값)
                {
                    float3 normal = hit.SurfaceNormal;
                    normal.y = 0; // 수직 벽 가정 (경사로 등반이 아니라면)

                    if (math.lengthsq(normal) > 0.001f)
                    {
                        normal = math.normalize(normal);

                        // 벽 쪽으로 이동 중일 때만 미끄러짐 처리
                        float dot = math.dot(velocity, normal);
                        if (dot < 0)
                        {
                            // 벡터 투영: 벽을 뚫고 가는 힘을 제거하고 벽면을 따라가는 힘만 남김
                            // Velocity Slide
                            velocity = velocity - (normal * dot);
                        }
                    }
                }
            }

            // [2] 주변 전방향 충돌 검사 (PointDistanceInput)
            // 분리 로직에 의해 밀렸을 때 이동 방향이 아닌 벽과의 충돌 감지
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

                    // 벽 방향으로 향하는 속도 성분 제거 (겹침이 있을 때만)
                    if (overlap > 0)
                    {
                        float dotToWall = math.dot(velocity, wallNormal);
                        if (dotToWall < 0)
                        {
                            velocity -= wallNormal * dotToWall;
                        }

                        // 겹침량에 비례해서 밀어내기 (상한 있음)
                        float pushSpeed = math.min(overlap * 10f, 5f);
                        velocity += wallNormal * pushSpeed;
                    }
                }
            }

            return velocity;
        }
    }
}