using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Shared;

namespace Server
{
    /// <summary>
    /// 경로 버퍼(PathWaypoint)를 관리하며 MovementWaypoints(현재/다음 목표)를 공급하는 시스템
    /// - PredictedMovementSystem이 웨이포인트에 도착하여 'Current'를 갱신하면,
    /// - 이 시스템이 감지하고 'Next'를 버퍼에서 꺼내 채워줌
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PathfindingSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [BurstCompile]
    public partial struct PathFollowSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<MovementGoal>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // 이동 중인(Enabled) 유닛만 처리
            foreach (var (goal, pathBuffer, waypoints) in
                SystemAPI.Query<
                        RefRW<MovementGoal>, 
                        DynamicBuffer<PathWaypoint>, 
                        RefRW<MovementWaypoints>>()
                    .WithAny<UnitTag, EnemyTag>())
            {
                // 경로 계산 중이거나 경로가 없으면 패스
                if (goal.ValueRO.IsPathDirty || pathBuffer.IsEmpty)
                    continue;

                int bufferIndex = goal.ValueRO.CurrentWaypointIndex;
                if (bufferIndex >= pathBuffer.Length) continue;

                // 1. 동기화 체크 (Sync Check)
                // PredictedMovementSystem이 웨이포인트에 도착해서 Next를 Current로 당겨썼는지 확인
                float3 bufferPos = pathBuffer[bufferIndex].Position;
                float3 currentTargetPos = waypoints.ValueRO.Current;

                // 버퍼의 현재 인덱스 위치와, 실제 유닛이 보고 있는 목표 위치가 다르면
                // 유닛이 이미 다음 단계로 넘어갔다는 뜻임 -> 인덱스 증가
                if (math.distancesq(bufferPos, currentTargetPos) > 0.001f)
                {
                    bufferIndex++;
                    goal.ValueRW.CurrentWaypointIndex = (byte)bufferIndex;
                }

                // 2. 다음 웨이포인트 공급 (Refill Next)
                // 유닛이 코너링을 할 수 있도록 Next 값을 미리 채워줌
                int nextIndex = bufferIndex + 1;
                
                if (nextIndex < pathBuffer.Length)
                {
                    // 아직 Next가 없거나, Next값이 갱신되어야 한다면 주입
                    if (!waypoints.ValueRO.HasNext ||
                        math.distancesq(waypoints.ValueRO.Next, pathBuffer[nextIndex].Position) > 0.001f)
                    {
                        waypoints.ValueRW.Next = pathBuffer[nextIndex].Position;
                        waypoints.ValueRW.HasNext = true;
                    }
                }
                else
                {
                    // 더 이상 갈 곳 없음 (마지막 구간)
                    if (waypoints.ValueRO.HasNext)
                    {
                        waypoints.ValueRW.HasNext = false;
                    }
                }
            }
        }
    }
}