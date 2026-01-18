using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using Shared;

namespace Server
{
    /// <summary>
    /// 공간 분할(Spatial Partitioning)을 위한 엔티티 데이터
    /// </summary>
    public struct SpatialEntry
    {
        public int EntityIndex; // NativeArray 인덱스 (직접 접근용)
        public Entity Entity;   // 자신 제외 체크용
        public byte Flags;      // bit0: IsEnemy, bit1: IsGathering
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PathfindingSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial struct PredictedMovementSystem : ISystem
    {
        private EntityQuery _movingQuery;     // 이동 연산 대상
        private EntityQuery _obstacleQuery;   // 회피 대상 (유닛 + 적)
        
        private const float CellSize = 3.0f;  // 공간 분할 격자 크기

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PhysicsWorldSingleton>();

            // 1. 이동 그룹: Waypoint가 활성화된 엔티티
            _movingQuery = state.GetEntityQuery(
                ComponentType.ReadWrite<LocalTransform>(),
                ComponentType.ReadWrite<PhysicsVelocity>(),
                ComponentType.ReadWrite<MovementWaypoints>(),
                ComponentType.ReadOnly<MovementDynamics>(),
                ComponentType.ReadOnly<ObstacleRadius>()
            );

            // 2. 장애물 그룹: 서로 밀어내야 하는 모든 대상 (Unit OR Enemy)
            //    * WithAny를 사용하여 한 번에 쿼리 (배열 병합 비용 제거)
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

            if (_obstacleQuery.CalculateEntityCount() == 0) return;

            // ------------------------------------------------------------------
            // [Step 1] 데이터 준비 (Zero-Copy)
            // ------------------------------------------------------------------
            // 쿼리에서 직접 배열 포인터를 가져옵니다. (복사 비용 0)
            var obstacleEntities = _obstacleQuery.ToEntityArray(Allocator.TempJob);
            var obstaclePositions = _obstacleQuery.ToComponentDataArray<LocalTransform>(Allocator.TempJob);
            var obstacleRadii = _obstacleQuery.ToComponentDataArray<ObstacleRadius>(Allocator.TempJob);

            int obstacleCount = obstacleEntities.Length;
            var spatialMap = new NativeParallelMultiHashMap<int, SpatialEntry>(obstacleCount, Allocator.TempJob);

            // ------------------------------------------------------------------
            // [Step 2] 공간 분할 맵 구축 (Grid Building)
            // ------------------------------------------------------------------
            var buildJob = new BuildSpatialGridJob
            {
                Entities = obstacleEntities,
                Positions = obstaclePositions,
                EnemyTagLookup = SystemAPI.GetComponentLookup<EnemyTag>(true),
                IntentLookup = SystemAPI.GetComponentLookup<UnitIntentState>(true),
                SpatialMap = spatialMap.AsParallelWriter(),
                CellSize = CellSize
            };

            JobHandle buildHandle = buildJob.Schedule(obstacleCount, 64, state.Dependency);

            // ------------------------------------------------------------------
            // [Step 3] 이동 및 충돌 처리 (Movement & Collision)
            // ------------------------------------------------------------------
            var wallFilter = new CollisionFilter
            {
                BelongsTo = ~0u,
                CollidesWith = (1u << 6) | (1u << 7), // Layer 6, 7 (벽)
                GroupIndex = 0
            };

            var moveJob = new KinematicMovementJob
            {
                DeltaTime = dt,
                // 이웃 조회용 데이터
                AllPositions = obstaclePositions,
                AllRadii = obstacleRadii,
                SpatialMap = spatialMap,
                // 상태 확인용 Lookup
                EnemyTagLookup = SystemAPI.GetComponentLookup<EnemyTag>(true),
                IntentLookup = SystemAPI.GetComponentLookup<UnitIntentState>(true),
                // 설정값
                CellSize = CellSize,
                SeparationStrength = 4.0f,
                CollisionWorld = physicsWorld.CollisionWorld,
                WallFilter = wallFilter
            };

            // 이동하는 엔티티(_movingQuery)에 대해서만 Job 실행
            state.Dependency = moveJob.ScheduleParallel(_movingQuery, buildHandle);

            // ------------------------------------------------------------------
            // [Step 4] 자원 해제 예약
            // ------------------------------------------------------------------
            obstacleEntities.Dispose(state.Dependency);
            obstaclePositions.Dispose(state.Dependency);
            obstacleRadii.Dispose(state.Dependency);
            spatialMap.Dispose(state.Dependency);
        }

        // 해시 유틸리티: 현재 위치 기준
        public static int GetCellHash(float3 pos, float cellSize)
        {
            return (int)math.hash(new int2((int)math.floor(pos.x / cellSize), (int)math.floor(pos.z / cellSize)));
        }
        
        // 해시 유틸리티: 이웃 셀 조회용 (Offset 포함)
        public static int GetCellHash(float3 pos, int xOff, int yOff, float cellSize)
        {
            return (int)math.hash(new int2((int)math.floor(pos.x / cellSize) + xOff, (int)math.floor(pos.z / cellSize) + yOff));
        }
    }

    /// <summary>
    /// Job: 각 엔티티를 공간 격자(HashMap)에 등록
    /// </summary>
    [BurstCompile]
    public struct BuildSpatialGridJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Entity> Entities;
        [ReadOnly] public NativeArray<LocalTransform> Positions;
        [ReadOnly] public ComponentLookup<EnemyTag> EnemyTagLookup;
        [ReadOnly] public ComponentLookup<UnitIntentState> IntentLookup;
        
        public NativeParallelMultiHashMap<int, SpatialEntry>.ParallelWriter SpatialMap;
        public float CellSize;

        public void Execute(int index)
        {
            Entity entity = Entities[index];
            float3 pos = Positions[index].Position;

            // 충돌 규칙 플래그 설정
            byte flags = 0;
            if (EnemyTagLookup.HasComponent(entity)) flags |= 1; // Enemy
            if (IntentLookup.TryGetComponent(entity, out UnitIntentState intent) && 
                intent.State == Intent.Gather) flags |= 2;       // Gathering

            // 해시맵 등록
            int hash = PredictedMovementSystem.GetCellHash(pos, CellSize);
            SpatialMap.Add(hash, new SpatialEntry
            {
                EntityIndex = index,
                Entity = entity,
                Flags = flags
            });
        }
    }

    /// <summary>
    /// Job: 실제 이동 계산 (경로 추적 -> 회피 -> 벽 충돌 -> 적용)
    /// </summary>
    [BurstCompile]
    public partial struct KinematicMovementJob : IJobEntity
    {
        public float DeltaTime;

        [ReadOnly] public NativeArray<LocalTransform> AllPositions;
        [ReadOnly] public NativeArray<ObstacleRadius> AllRadii;
        [ReadOnly] public NativeParallelMultiHashMap<int, SpatialEntry> SpatialMap;
        
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

            // ----------------------------------------------------------
            // 1. Waypoint 추적 (경로 따라가기)
            // ----------------------------------------------------------
            float3 toTarget = targetPos - currentPos;
            float dist = math.length(toTarget);

            // 다음 경유지 전환 체크
            if (waypoints.HasNext && dist < 0.5f)
            {
                waypoints.Current = waypoints.Next;
                waypoints.HasNext = false;
                
                // 목표 갱신
                targetPos = waypoints.Current;
                targetPos.y = currentPos.y;
                toTarget = targetPos - currentPos;
                dist = math.length(toTarget);
            }

            // 최종 도착 체크
            float arrivalR = waypoints.ArrivalRadius > 0 ? waypoints.ArrivalRadius : 0.1f;
            if (!waypoints.HasNext && dist < arrivalR)
            {
                velocity.Linear = float3.zero;
                return;
            }

            // ----------------------------------------------------------
            // 2. 가속/감속 처리 (Velocity Calculation)
            // ----------------------------------------------------------
            float3 moveDir = dist > 0.001f ? toTarget / dist : float3.zero;
            float targetSpeed = dynamics.MaxSpeed;

            // 도착지점 근처 감속 (Arrival Braking)
            if (!waypoints.HasNext)
            {
                float decel = math.max(0.1f, dynamics.Deceleration);
                float slowingDist = (dynamics.MaxSpeed * dynamics.MaxSpeed) / (2f * decel);
                if (dist < slowingDist)
                    targetSpeed = math.lerp(0, dynamics.MaxSpeed, dist / slowingDist);
            }

            // 현재 속도에서 목표 속도로 보간
            float currentSpeed = math.length(velocity.Linear);
            float speedDiff = targetSpeed - currentSpeed;
            float accelRate = speedDiff > 0 ? dynamics.Acceleration : dynamics.Deceleration;
            float newSpeed = currentSpeed + math.sign(speedDiff) * math.min(math.abs(speedDiff), accelRate * DeltaTime);
            
            float3 desiredVelocity = moveDir * newSpeed;
            desiredVelocity.y = 0;

            // ----------------------------------------------------------
            // 3. 유닛 간 회피 (Separation)
            // ----------------------------------------------------------
            byte myFlags = 0;
            if (EnemyTagLookup.HasComponent(entity)) myFlags |= 1;
            if (IntentLookup.TryGetComponent(entity, out UnitIntentState intent) && intent.State == Intent.Gather) myFlags |= 2;

            float3 separationForce = CalculateSeparation(currentPos, obstacleRadius.Radius, entity, myFlags);
            float3 finalVelocity = desiredVelocity + (separationForce * SeparationStrength);

            // 속도 캡핑 (튕김 방지)
            float maxLimit = dynamics.MaxSpeed * 1.5f;
            if (math.lengthsq(finalVelocity) > maxLimit * maxLimit)
            {
                finalVelocity = math.normalizesafe(finalVelocity) * maxLimit;
            }

            // ----------------------------------------------------------
            // 4. 벽 충돌 처리 (Physics Wall Collision)
            // ----------------------------------------------------------
            finalVelocity = ResolveWallCollision(currentPos, finalVelocity, obstacleRadius.Radius, DeltaTime);

            // ----------------------------------------------------------
            // 5. 최종 적용 (Apply)
            // ----------------------------------------------------------
            transform.Position += finalVelocity * DeltaTime;
            velocity.Linear = finalVelocity;
            velocity.Angular = float3.zero;

            // 이동 방향으로 회전
            if (math.lengthsq(finalVelocity) > 0.01f)
            {
                quaternion targetRot = quaternion.LookRotationSafe(math.normalizesafe(finalVelocity), math.up());
                transform.Rotation = math.slerp(transform.Rotation, targetRot, dynamics.RotationSpeed * DeltaTime);
            }
        }

        // 주변 3x3 셀을 검사하여 밀어내는 힘 계산
        private float3 CalculateSeparation(float3 myPos, float myRadius, Entity myEntity, byte myFlags)
        {
            float3 separation = float3.zero;
            bool iAmEnemy = (myFlags & 1) != 0;
            bool iAmGathering = (myFlags & 2) != 0;

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

                            // 충돌 규칙: 적은 항상 충돌, 아군은 둘 다 채집 중이 아닐 때만 충돌
                            bool isEnemy = (neighbor.Flags & 1) != 0;
                            bool isGathering = (neighbor.Flags & 2) != 0;
                            
                            bool shouldCollide = iAmEnemy || isEnemy || (!iAmGathering && !isGathering);
                            if (!shouldCollide) continue;

                            // 위치 데이터 조회
                            float3 otherPos = AllPositions[neighbor.EntityIndex].Position;
                            float otherRadius = AllRadii[neighbor.EntityIndex].Radius;

                            float3 toOther = myPos - otherPos; 
                            toOther.y = 0;
                            
                            float distSq = math.lengthsq(toOther);
                            float combinedRadius = myRadius + otherRadius + 0.3f;

                            // 겹쳤을 경우 밀어냄
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

        // 물리 세계 쿼리를 사용한 벽 충돌 처리 (메모리 할당 없음)
        private float3 ResolveWallCollision(float3 currentPos, float3 velocity, float radius, float dt)
        {
            float speed = math.length(velocity);
            if (speed < 0.001f) return velocity;

            float3 nextPos = currentPos + (velocity * dt);

            // 점 거리 체크 (Point Distance Check)
            var pointInput = new PointDistanceInput
            {
                Position = nextPos + new float3(0, 0.5f, 0), // y오프셋으로 바닥 걸림 방지
                MaxDistance = radius + 0.05f,                // 반지름 + 여유값
                Filter = WallFilter
            };

            if (CollisionWorld.CalculateDistance(pointInput, out DistanceHit hit))
            {
                // 벽이 내 반지름 안쪽으로 들어왔다면 충돌
                if (hit.Distance < radius)
                {
                    float3 normal = hit.SurfaceNormal;
                    normal.y = 0; 
                    
                    if (math.lengthsq(normal) > 0.001f)
                    {
                        normal = math.normalize(normal);
                        // 벽 방향으로 진행 중이라면 벡터 투영(Slide)
                        float dot = math.dot(velocity, normal);
                        if (dot < 0)
                        {
                            // 벽에 정면으로 부딪힐수록 더 많이 감속 (dot이 -1에 가까울수록 정면)
                            float3 slideVelocity = velocity - (normal * dot);
                            // 정면 충돌 시 완전 정지, 비스듬할수록 속도 유지
                            float slideRatio = math.length(slideVelocity) / (math.length(velocity) + 0.001f);
                            return slideVelocity * slideRatio;
                        }
                    }
                }
            }
            return velocity;
        }
    }
}