using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;
using Unity.Mathematics;
using Unity.Physics;
using Shared;

/*
 * 실행 환경: 클라이언트 + 서버 (예측 시뮬레이션)
 * RTS 유닛의 이동 로직을 처리하는 시스템
 *    명령 처리: RTSCommand 버퍼에서 Move 명령 읽기 → MoveTarget 설정
 *    이동 로직: MoveTarget을 향해 직선 이동, 도착하면 isValid = false
 *    Separation Force: 주변 유닛과 겹치지 않도록 밀어내기 힘 적용
 */
[UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
public partial struct NetcodePlayerMovementSystem : ISystem
{
    // Separation Force 설정
    private const float SeparationRadius = 1.5f;      // 유닛 간 분리 감지 반경
    private const float SeparationStrength = 2.0f;    // 분리 힘 강도

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
        float arrivalThreshold = 0.5f;
        float unitRadius = 0.5f; // 유닛 충돌 반지름

        var networkTime = SystemAPI.GetSingleton<NetworkTime>();
        var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
        var collisionWorld = physicsWorld.CollisionWorld;

        // Unit(Layer 7) → Structure(Layer 6) 충돌 필터
        var structureCollisionFilter = new CollisionFilter
        {
            BelongsTo = 1u << 7,    // Unit
            CollidesWith = 1u << 6, // Structure
            GroupIndex = 0
        };

        // Unit(Layer 7) ↔ Unit(Layer 7) 충돌 필터 (Separation Force용)
        var unitCollisionFilter = new CollisionFilter
        {
            BelongsTo = 1u << 7,    // Unit
            CollidesWith = 1u << 7, // Unit
            GroupIndex = 0
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
            // 1. 명령(Command) 처리: 입력 버퍼에서 목표지점 꺼내오기
            if (inputBuffer.GetDataAtTick(networkTime.ServerTick, out RTSCommand command))
            {
                if (command.CommandType == RTSCommandType.Move)
                {
                    // PathfindingState가 있으면 경로 계산 요청
                    if (SystemAPI.HasComponent<PathfindingState>(entity))
                    {
                        var pathState = SystemAPI.GetComponentRW<PathfindingState>(entity);
                        pathState.ValueRW.FinalDestination = command.TargetPosition;
                        pathState.ValueRW.NeedsPath = true;
                        pathState.ValueRW.CurrentWaypointIndex = 0;
                        pathState.ValueRW.TotalWaypoints = 0;

                        // MoveTarget은 PathfindingSystem에서 설정
                    }
                    else
                    {
                        // 경로 탐색이 없는 유닛은 직접 MoveTarget 설정
                        moveTarget.ValueRW.position = command.TargetPosition;
                        moveTarget.ValueRW.isValid = true;
                    }
                }
            }

            // 2. 이동 로직
            if (moveTarget.ValueRO.isValid)
            {
                float3 currentPos = localTransform.ValueRO.Position;
                float3 targetPos = moveTarget.ValueRO.position;

                // 목표 지점의 Y 현재 Y와 일치
                targetPos.y = currentPos.y;

                float distance = math.distance(currentPos, targetPos);

                // 도착 판단
                if (distance < arrivalThreshold)
                {
                    moveTarget.ValueRW.isValid = false;

                    // 도착 시 떨림 방지를 위해 위치 강제 고정
                    localTransform.ValueRW.Position = targetPos;
                }
                else
                {
                    float moveStep = movementSpeed.ValueRO.Value * deltaTime;

                    if (distance <= moveStep)
                    {
                         localTransform.ValueRW.Position = targetPos;
                         moveTarget.ValueRW.isValid = false;
                    }
                    else
                    {
                        float3 direction = math.normalize(targetPos - currentPos);

                        // 3. Separation Force 계산 (유닛 간 밀어내기)
                        float3 separationForce = CalculateSeparationForce(
                            currentPos, entity, ref collisionWorld, unitCollisionFilter);

                        // 최종 이동 방향 = 목표 방향 + 분리 힘
                        float3 finalDirection = direction + separationForce * SeparationStrength;
                        if (math.lengthsq(finalDirection) > 0.001f)
                            finalDirection = math.normalize(finalDirection);

                        float3 desiredMovement = finalDirection * moveStep;
                        float3 finalMovement = desiredMovement;

                        // 4. 건물 충돌 감지 (RayCast)
                        float castDistance = moveStep + unitRadius;
                        var raycastInput = new RaycastInput
                        {
                            Start = currentPos,
                            End = currentPos + finalDirection * castDistance,
                            Filter = structureCollisionFilter
                        };

                        if (collisionWorld.CastRay(raycastInput, out RaycastHit hit))
                        {
                            float hitDistance = hit.Fraction * castDistance;

                            // 유닛 반지름보다 가까우면 충돌
                            if (hitDistance < unitRadius + moveStep)
                            {
                                // 충돌 시 슬라이딩 계산
                                float3 hitNormal = hit.SurfaceNormal;
                                hitNormal.y = 0; // 수평면에서만 슬라이딩
                                hitNormal = math.normalizesafe(hitNormal);

                                // 슬라이딩 방향 = 원래 방향에서 법선 방향 성분 제거
                                float3 slideDirection = direction - math.dot(direction, hitNormal) * hitNormal;

                                if (math.lengthsq(slideDirection) > 0.001f)
                                {
                                    slideDirection = math.normalize(slideDirection);
                                    finalMovement = slideDirection * moveStep;

                                    // 슬라이딩 방향으로 다시 충돌 체크
                                    var slideRayInput = new RaycastInput
                                    {
                                        Start = currentPos,
                                        End = currentPos + slideDirection * castDistance,
                                        Filter = structureCollisionFilter
                                    };

                                    if (collisionWorld.CastRay(slideRayInput, out RaycastHit slideHit))
                                    {
                                        float slideHitDist = slideHit.Fraction * castDistance;
                                        if (slideHitDist < unitRadius + moveStep)
                                        {
                                            // 슬라이딩도 막힘 - 충돌 직전까지만 이동
                                            float safeMove = math.max(0, slideHitDist - unitRadius);
                                            finalMovement = slideDirection * safeMove;
                                        }
                                    }
                                }
                                else
                                {
                                    // 슬라이딩 불가 (정면 충돌) - 이동 안함
                                    finalMovement = float3.zero;
                                }
                            }
                        }

                        // 최종 이동 적용
                        localTransform.ValueRW.Position += finalMovement;

                        // 회전 (원래 방향으로)
                        if (math.lengthsq(direction) > 0.001f)
                        {
                            localTransform.ValueRW.Rotation = quaternion.LookRotationSafe(direction, math.up());
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// 주변 유닛과의 Separation Force 계산
    /// 가까운 유닛이 있으면 반대 방향으로 밀어내는 힘 반환
    /// </summary>
    private static float3 CalculateSeparationForce(
        float3 currentPos,
        Entity selfEntity,
        ref CollisionWorld collisionWorld,
        CollisionFilter unitFilter)
    {
        float3 separationForce = float3.zero;

        // PointDistanceInput으로 주변 유닛 탐지
        var pointInput = new PointDistanceInput
        {
            Position = currentPos,
            MaxDistance = SeparationRadius,
            Filter = unitFilter
        };

        var hits = new NativeList<DistanceHit>(8, Allocator.Temp);

        if (collisionWorld.CalculateDistance(pointInput, ref hits))
        {
            foreach (var hit in hits)
            {
                // 자기 자신 제외
                if (hit.Entity == selfEntity)
                    continue;

                float3 awayFromNeighbor = currentPos - hit.Position;
                awayFromNeighbor.y = 0; // 수평면에서만 계산
                float dist = math.length(awayFromNeighbor);

                if (dist > 0.01f && dist < SeparationRadius)
                {
                    // 거리에 반비례하는 밀어내기 힘
                    // 가까울수록 강하게 밀어냄
                    float strength = (SeparationRadius - dist) / SeparationRadius;
                    separationForce += math.normalize(awayFromNeighbor) * strength;
                }
            }
        }

        hits.Dispose();

        return separationForce;
    }
}