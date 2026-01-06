#if LEGACY_MOVEMENT_SYSTEM  // 조건부 컴파일로 완전 비활성화
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;
using Unity.Mathematics;
using Unity.Physics;
using Shared;

namespace Shared
{
    [UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
    [BurstCompile]
    public partial struct NetcodeUnitMovementSystem : ISystem
    {
        private const float SeparationRadius = 0.8f;   // 유닛 반경(0.5) * 2 보다 약간 작게 → 거의 닿을 때만 밀어냄
        private const float SeparationStrength = 1.0f;
        private const float CornerRadius = 1.2f;      // 코너링 감지 거리
        private const float ArrivalThreshold = 0.5f;  // 최종 도착 판정 거리
        private const float WallCheckDistance = 1.0f; // 벽 감지 레이 길이

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkTime>();
            state.RequireForUpdate<PhysicsWorldSingleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float deltaTime = SystemAPI.Time.DeltaTime;
            var networkTime = SystemAPI.GetSingleton<NetworkTime>();
            var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
            var collisionWorld = physicsWorld.CollisionWorld;

            // [최적화 1] 충돌 필터 설정
            // UnitFilter: 유닛끼리 밀어내기용 (상대방도 11번이어야 함)
            var unitCollisionFilter = new CollisionFilter
            {
                BelongsTo = 1u << 11,
                CollidesWith = 1u << 11 | 1u << 12,
                GroupIndex = 0
            };

            // StructureFilter: 벽 감지용
            var structureCollisionFilter = new CollisionFilter
            {
                BelongsTo = 1u << 11,       
                CollidesWith = 1u << 7, 
                GroupIndex = 0
            };

            // [최적화 2] Query 루프 실행
            foreach ((
                 RefRW<MovementDestination> moveTarget,
                 RefRW<LocalTransform> localTransform,
                 DynamicBuffer<UnitCommand> inputBuffer,
                 RefRO<MovementSpeed> movementSpeed,
                 Entity entity)
                 in SystemAPI.Query<
                     RefRW<MovementDestination>,
                     RefRW<LocalTransform>,
                     DynamicBuffer<UnitCommand>,
                     RefRO<MovementSpeed>>()
                     .WithAll<Simulate>() // 시뮬레이션 대상만
                     .WithEntityAccess())
            {
                // 1. 명령 처리 (서버 틱 기반)
                ProcessCommands(inputBuffer, networkTime.ServerTick, entity, ref moveTarget.ValueRW, ref state);

                if (!moveTarget.ValueRO.IsValid) continue;

                // 2. 이동 로직 수행
                MoveUnit(
                    ref localTransform.ValueRW, 
                    ref moveTarget.ValueRW, 
                    movementSpeed.ValueRO.Value, 
                    deltaTime, 
                    entity, 
                    ref collisionWorld, 
                    unitCollisionFilter, 
                    structureCollisionFilter
                );
            }
        }

        // 명령 처리 분리 (가독성 향상)
        private void ProcessCommands(
            DynamicBuffer<UnitCommand> inputBuffer, 
            NetworkTick serverTick, 
            Entity entity, 
            ref MovementDestination moveTarget,
            ref SystemState state)
        {
            if (inputBuffer.GetDataAtTick(serverTick, out UnitCommand command))
            {
                if (command.CommandType == UnitCommandType.Move)
                {
                    if (SystemAPI.HasComponent<PathfindingState>(entity))
                    {
                        var pathStateLookup = SystemAPI.GetComponentLookup<PathfindingState>(false);
                        if(pathStateLookup.HasComponent(entity))
                        {
                            var pathState = pathStateLookup[entity];
                            float dist = math.distance(pathState.FinalDestination, command.TargetPosition);

                            if (dist > 0.1f)
                            {
                                pathState.FinalDestination = command.TargetPosition;
                                pathState.NeedsPath = true;
                                pathState.CurrentWaypointIndex = 0;
                                pathStateLookup[entity] = pathState; // 값 갱신
                            }
                        }
                    }
                    else
                    {
                        moveTarget.Position = command.TargetPosition;
                        moveTarget.IsValid = true;
                        moveTarget.HasNextPosition = false;
                    }
                }
            }
        }

        // 실제 이동 및 물리 처리
        private void MoveUnit(
            ref LocalTransform transform, 
            ref MovementDestination moveTarget, 
            float speed, 
            float deltaTime,
            Entity entity,
            ref CollisionWorld collisionWorld,
            CollisionFilter unitFilter,
            CollisionFilter structureFilter)
        {
            float3 currentPos = transform.Position;
            float3 targetPos = moveTarget.Position;
            targetPos.y = currentPos.y; // Y축 고정 (평면 이동)

            float distance = math.distance(currentPos, targetPos);

            // A. 코너링 및 도착 처리
            if (moveTarget.HasNextPosition && distance < CornerRadius)
            {
                moveTarget.Position = moveTarget.NextPosition;
                moveTarget.HasNextPosition = false;
                
                // 타겟 변경 후 거리 재계산
                targetPos = moveTarget.Position;
                targetPos.y = currentPos.y;
                distance = math.distance(currentPos, targetPos);
            }
            else if (!moveTarget.HasNextPosition && distance < ArrivalThreshold)
            {
                moveTarget.IsValid = false;
                transform.Position = targetPos; // 깔끔하게 도착 지점에 안착
                return;
            }

            if (distance <= 0.001f) return;

            // B. 방향 계산
            float3 toTarget = targetPos - currentPos;
            float toTargetLengthSq = math.lengthsq(toTarget);
            if (toTargetLengthSq < 0.0001f) return; // 이미 목표 위치에 있음

            float3 direction = toTarget * math.rsqrt(toTargetLengthSq);

            // C. 유닛 간 밀어내기 (Separation)
            float3 separationForce = CalculateSeparationForce(currentPos, entity, ref collisionWorld, unitFilter);

            // 최종 이동 방향 (목표 방향 + 밀어내기)
            float3 combinedDir = direction + (separationForce * SeparationStrength);
            float combinedLengthSq = math.lengthsq(combinedDir);

            // 합산 벡터가 0에 가까우면 목표 방향만 사용 (NaN 방지)
            float3 finalDirection = combinedLengthSq > 0.0001f
                ? combinedDir * math.rsqrt(combinedLengthSq)
                : direction;
            float moveStep = speed * deltaTime;

            // D. [중요] 건물 충돌 감지 및 슬라이딩 (Sliding)
            // 이동하려는 방향으로 레이를 쏴서 벽이 있는지 확인
            var rayInput = new RaycastInput
            {
                Start = currentPos,
                End = currentPos + (finalDirection * WallCheckDistance), // 살짝 앞까지 검사
                Filter = structureFilter
            };

            if (collisionWorld.CastRay(rayInput, out var hit))
            {
                // 벽과 충돌했다면, 벽의 법선(Normal)을 이용해 미끄러지는 벡터 구하기
                // 공식: V_slide = V - (V · N) * N
                float3 normal = hit.SurfaceNormal;
                float dot = math.dot(finalDirection, normal);
                
                // 벽 안쪽으로 파고드는 방향일 때만 투영 (벽에서 멀어지는 중이면 간섭 X)
                if (dot < 0)
                {
                    float3 slideDir = finalDirection - (dot * normal);
                    float slideLengthSq = math.lengthsq(slideDir);
                    if (slideLengthSq > 0.0001f)
                    {
                        finalDirection = slideDir * math.rsqrt(slideLengthSq);
                    }
                }
            }

            // E. 최종 위치 및 회전 적용
            transform.Position += finalDirection * moveStep;
            
            // 회전이 튀는 것 방지 (벡터가 유효할 때만 회전)
            if (math.lengthsq(finalDirection) > 0.001f)
            {
                transform.Rotation = quaternion.LookRotationSafe(finalDirection, math.up());
            }
        }

        /// <summary>                                                                                                            
        /// 주변 유닛과의 거리 기반 반발력 계산                                                                                  
        /// </summary>                                                                                                           
        private static float3 CalculateSeparationForce(                                                                          
            float3 currentPos,                                                                                                   
            Entity selfEntity,                                                                                                   
            ref CollisionWorld collisionWorld,                                                                                   
            CollisionFilter unitFilter)  
        {
            float3 separationForce = float3.zero;                                                                                
                                                                                                                                 
            // PointDistanceInput으로 주변 콜라이더 탐색                                                                         
            var pointDistanceInput = new PointDistanceInput                                                                      
            {                                                                                                                    
                Position = currentPos,                                                                                           
                MaxDistance = SeparationRadius,                                                                                  
                Filter = unitFilter                                                                                              
            };                                                                                                                   
                                                                                                                                 
            // 임시 버퍼에 결과 수집                                                                                             
            var hits = new NativeList<DistanceHit>(16, Allocator.Temp);                                                          
                                                                                                                                 
            if (collisionWorld.CalculateDistance(pointDistanceInput, ref hits))                                                  
            {                                                                                                                    
                for (int i = 0; i < hits.Length; i++)                                                                            
                {                                                                                                                
                    var hit = hits[i];                                                                                           
                                                                                                                                 
                    // 자기 자신 제외                                                                                            
                    if (hit.Entity == selfEntity) continue;                                                                      
                                                                                                                                 
                    // 거리가 0에 가까우면 임의의 방향으로 밀어내기                                                              
                    float dist = hit.Distance;                                                                                   
                    if (dist < 0.01f)                                                                                            
                    {                                                                                                            
                        // 랜덤 대신 Entity 인덱스 기반으로 방향 결정 (결정적)                                                   
                        float angle = (selfEntity.Index % 360) * math.PI / 180f;                                                 
                        separationForce += new float3(math.cos(angle), 0f, math.sin(angle));                                     
                        continue;                                                                                                
                    }                                                                                                            
                                                                                                                                 
                    // 반발 방향: 상대방 → 나 (hit.Position은 상대방 콜라이더의 가장 가까운 점)                                  
                    float3 awayDir = currentPos - hit.Position;                                                                  
                    awayDir.y = 0f; // Y축 고정                                                                                  
                                                                                                                                 
                    float awayLength = math.length(awayDir);                                                                     
                    if (awayLength < 0.001f) continue;                                                                           
                                                                                                                                 
                    // 정규화 후 거리에 반비례하는 힘 적용                                                                       
                    // 가까울수록 강하게 밀어냄 (1 - dist/radius)                                                                
                    float strength = 1f - (dist / SeparationRadius);                                                             
                    separationForce += (awayDir / awayLength) * strength;                                                        
                }                                                                                                                
            }                                                                                                                    
                                                                                                                                 
            hits.Dispose();                                                                                                      
            return separationForce;     
        }
    }
}
#endif  // LEGACY_MOVEMENT_SYSTEM