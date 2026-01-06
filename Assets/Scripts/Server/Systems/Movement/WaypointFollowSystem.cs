using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Shared;

namespace Server
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PathfindingSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [BurstCompile]
    public partial struct WaypointFollowSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PathfindingState>();
        }

        public void OnUpdate(ref SystemState state)
        {
            foreach (var (pathState, pathBuffer, moveTarget, transform) in
                SystemAPI.Query<RefRW<PathfindingState>, DynamicBuffer<PathWaypoint>, RefRW<MovementDestination>, RefRO<LocalTransform>>()
                    .WithAll<UnitTag>())
            {
                if (pathState.ValueRO.TotalWaypoints == 0 || pathBuffer.IsEmpty)
                    continue;

                int currentIndex = pathState.ValueRO.CurrentWaypointIndex;
                
                // 1. 유닛이 클라이언트 예측으로 이미 타겟을 다음 웨이포인트로 바꿨는지 확인
                // (MoveTarget.position이 현재 인덱스의 위치와 다르면, 유닛이 이미 코너를 돌았다는 뜻)
                if (currentIndex < pathBuffer.Length)
                {
                    float3 bufferPos = pathBuffer[currentIndex].Position;
                    float3 currentTargetPos = moveTarget.ValueRO.Position;
                    
                    // 오차 범위(float 정밀도) 내에서 다른지 체크
                    if (math.distancesq(bufferPos, currentTargetPos) > 0.1f)
                    {
                        // 유닛이 이미 다음거로 넘어갔음 -> 서버 인덱스 증가
                        currentIndex++;
                        pathState.ValueRW.CurrentWaypointIndex = (byte)currentIndex;
                    }
                }

                // 범위 체크
                if (currentIndex >= pathBuffer.Length)
                {
                    // 도착 완료 상태
                    // MoveTarget.isValid는 MovementSystem에서 끄도록 둠
                    continue; 
                }

                // 2. 현재 상태에 맞춰 MoveTarget 데이터 갱신 (Look-ahead)
                // 현재 목표가 올바른지 확인 및 재설정
                moveTarget.ValueRW.Position = pathBuffer[currentIndex].Position;
                moveTarget.ValueRW.IsValid = true;

                // [핵심] 다음 웨이포인트 미리 넣어주기
                int nextIndex = currentIndex + 1;
                if (nextIndex < pathBuffer.Length)
                {
                    moveTarget.ValueRW.NextPosition = pathBuffer[nextIndex].Position;
                    moveTarget.ValueRW.HasNextPosition = true;
                }
                else
                {
                    // 다음 경로 없음 (마지막 구간)
                    moveTarget.ValueRW.HasNextPosition = false;
                }
            }
        }
    }
}