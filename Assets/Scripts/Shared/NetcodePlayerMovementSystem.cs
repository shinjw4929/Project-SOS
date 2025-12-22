using Unity.Burst;
using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;
using Unity.Mathematics;
using Shared; // RTSCommand 사용을 위해 추가

[UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
public partial struct NetcodePlayerMovementSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<NetworkTime>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        float moveSpeed = 10f;
        float arrivalThreshold = 0.1f; // 더 정밀하게 변경

        var networkTime = SystemAPI.GetSingleton<NetworkTime>();

        foreach ((
             RefRW<MoveTarget> moveTarget,
             RefRW<LocalTransform> localTransform,
             DynamicBuffer<RTSCommand> inputBuffer) // 명령 버퍼 추가
             in SystemAPI.Query<
                 RefRW<MoveTarget>,
                 RefRW<LocalTransform>,
                 DynamicBuffer<RTSCommand>>().WithAll<Simulate>())
        {
            // 1. 명령(Command) 처리: 입력 버퍼에서 목표지점 꺼내오기
            if (inputBuffer.GetDataAtTick(networkTime.ServerTick, out RTSCommand command))
            {
                if (command.commandType == RTSCommandType.Move)
                {
                    moveTarget.ValueRW.position = command.targetPosition;
                    moveTarget.ValueRW.isValid = true;
                }
            }

            // 2. 이동 로직
            if (moveTarget.ValueRO.isValid)
            {
                float3 currentPos = localTransform.ValueRO.Position;
                float3 targetPos = moveTarget.ValueRO.position;

                // [핵심 해결] 높이(Y) 차이 무시!
                // 목표 지점의 Y를 내 높이와 똑같이 맞춰서, 수평 거리만 따지게 함
                targetPos.y = currentPos.y; 

                float distance = math.distance(currentPos, targetPos);

                // 도착 판단
                if (distance < arrivalThreshold)
                {
                    moveTarget.ValueRW.isValid = false;
                    
                    // [옵션] 도착 시 미세한 떨림 방지를 위해 위치 강제 고정
                    // localTransform.ValueRW.Position = targetPos; 
                }
                else
                {
                    // [추가] 오버슈팅 방지 (이번 프레임 이동량이 남은 거리보다 크면 바로 도착 처리)
                    float moveStep = moveSpeed * deltaTime;
                    
                    if (distance <= moveStep)
                    {
                         // 바로 도착 지점으로 이동 후 정지
                         localTransform.ValueRW.Position = targetPos;
                         moveTarget.ValueRW.isValid = false;
                    }
                    else
                    {
                        // 이동
                        float3 direction = math.normalize(targetPos - currentPos);
                        localTransform.ValueRW.Position += direction * moveStep;
                        
                        // 회전
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