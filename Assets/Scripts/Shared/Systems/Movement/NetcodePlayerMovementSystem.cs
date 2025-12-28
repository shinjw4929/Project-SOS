using Unity.Burst;
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
 */
[UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
public partial struct NetcodePlayerMovementSystem : ISystem
{
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
        var collisionFilter = new CollisionFilter
        {
            BelongsTo = 1u << 7,    // Unit
            CollidesWith = 1u << 6, // Structure
            GroupIndex = 0
        };

        foreach ((
             RefRW<MoveTarget> moveTarget,
             RefRW<LocalTransform> localTransform,
             DynamicBuffer<RTSCommand> inputBuffer,
             RefRO<MovementSpeed> movementSpeed)
             in SystemAPI.Query<
                 RefRW<MoveTarget>,
                 RefRW<LocalTransform>,
                 DynamicBuffer<RTSCommand>,
                 RefRO<MovementSpeed>>().WithAll<Simulate>())
        {
            // 1. 명령(Command) 처리: 입력 버퍼에서 목표지점 꺼내오기
            if (inputBuffer.GetDataAtTick(networkTime.ServerTick, out RTSCommand command))
            {
                if (command.CommandType == RTSCommandType.Move)
                {
                    moveTarget.ValueRW.position = command.TargetPosition;
                    moveTarget.ValueRW.isValid = true;
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
                        float3 desiredMovement = direction * moveStep;
                        float3 finalMovement = desiredMovement;

                        // 3. 충돌 감지 (RayCast)
                        float castDistance = moveStep + unitRadius;
                        var raycastInput = new RaycastInput
                        {
                            Start = currentPos,
                            End = currentPos + direction * castDistance,
                            Filter = collisionFilter
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
                                        Filter = collisionFilter
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
}