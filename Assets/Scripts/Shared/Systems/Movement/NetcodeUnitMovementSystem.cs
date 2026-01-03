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
    public partial struct NetcodeUnitMovementSystem : ISystem
    {
        private const float SeparationRadius = 1.5f;
        private const float SeparationStrength = 2.0f;
        
        // [추가됨] 코너링을 시작할 거리 (이 거리 안으로 들어오면 다음 지점으로 조기 전환)
        private const float CornerRadius = 1.2f; 
        private const float ArrivalThreshold = 0.5f;

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

            // 7: Structure
            // 11: Unit
            var structureCollisionFilter = new CollisionFilter
            {
                BelongsTo = 1u << 11, CollidesWith = 1u << 7, GroupIndex = 0
            };
            var unitCollisionFilter = new CollisionFilter
            {
                BelongsTo = 1u << 11, CollidesWith = 1u << 11, GroupIndex = 0
            };

            foreach ((
                 RefRW<MoveTarget> moveTarget,
                 RefRW<LocalTransform> localTransform,
                 DynamicBuffer<RTSCommand> inputBuffer,
                 RefRO<MovementSpeed> movementSpeed,
                 Entity entity)
                 in SystemAPI.Query<
                     RefRW<MoveTarget>,
                     RefRW<LocalTransform>,
                     DynamicBuffer<RTSCommand>,
                     RefRO<MovementSpeed>>().WithAll<Simulate>().WithEntityAccess())
            {
                // 1. 명령 처리 (기존과 동일)
                if (inputBuffer.GetDataAtTick(networkTime.ServerTick, out RTSCommand command))
                {
                    if (command.CommandType == RTSCommandType.Move)
                    {
                        if (SystemAPI.HasComponent<PathfindingState>(entity))
                        {
                            var pathState = SystemAPI.GetComponentRW<PathfindingState>(entity);
                            pathState.ValueRW.FinalDestination = command.TargetPosition;
                            pathState.ValueRW.NeedsPath = true;
                            // 경로 재계산 시 초기화
                            pathState.ValueRW.CurrentWaypointIndex = 0; 
                        }
                        else
                        {
                            moveTarget.ValueRW.position = command.TargetPosition;
                            moveTarget.ValueRW.isValid = true;
                            moveTarget.ValueRW.HasNextPosition = false; // 직접 이동 시 다음 경로 없음
                        }
                    }
                }

                // 2. 이동 로직
                if (moveTarget.ValueRO.isValid)
                {
                    float3 currentPos = localTransform.ValueRO.Position;
                    float3 targetPos = moveTarget.ValueRO.position;
                    targetPos.y = currentPos.y;

                    float distance = math.distance(currentPos, targetPos);

                    // [핵심 변경] 하이브리드 코너링 로직
                    // 다음 목표가 있고, 코너링 반경 내에 진입했다면 미리 타겟 변경
                    if (moveTarget.ValueRO.HasNextPosition && distance < CornerRadius)
                    {
                        // 즉시 다음 웨이포인트로 타겟 변경 (서버 응답 대기 X)
                        moveTarget.ValueRW.position = moveTarget.ValueRO.NextPosition;
                        moveTarget.ValueRW.HasNextPosition = false; // 소비함
                        
                        // 갱신된 타겟으로 거리 재계산
                        targetPos = moveTarget.ValueRO.position;
                        targetPos.y = currentPos.y;
                        distance = math.distance(currentPos, targetPos);
                    }
                    // 최종 도착 처리 (다음 웨이포인트가 없을 때만)
                    else if (!moveTarget.ValueRO.HasNextPosition && distance < ArrivalThreshold)
                    {
                        moveTarget.ValueRW.isValid = false;
                        localTransform.ValueRW.Position = targetPos;
                        continue; // 이동 종료
                    }

                    // ... (이하 물리 이동, Separation, Sliding 로직은 기존 코드 유지) ...
                    float moveStep = movementSpeed.ValueRO.Value * deltaTime;
                    
                    if (distance > 0.001f)
                    {
                        float3 direction = math.normalize(targetPos - currentPos);
                        
                        // Separation Force
                        float3 separationForce = CalculateSeparationForce(currentPos, entity, ref collisionWorld, unitCollisionFilter);
                        float3 finalDirection = math.normalize(direction + separationForce * SeparationStrength);
                        
                        // 이동 및 충돌 처리 로직 (기존과 동일하게 사용)
                        // ... (생략된 Sliding/Collision 로직 삽입) ...

                         // 간략화된 이동 적용 (전체 코드는 기존 파일 참조)
                        localTransform.ValueRW.Position += finalDirection * moveStep;
                        localTransform.ValueRW.Rotation = quaternion.LookRotationSafe(direction, math.up());
                    }
                }
            }
        }
        
        // CalculateSeparationForce 메서드는 기존과 동일하므로 생략
        private static float3 CalculateSeparationForce(float3 currentPos, Entity selfEntity, ref CollisionWorld collisionWorld, CollisionFilter unitFilter)
        {
            // (기존 코드 사용)
            return float3.zero; 
        }
    }
}