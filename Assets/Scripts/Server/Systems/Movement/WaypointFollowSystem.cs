using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Shared;

namespace Server
{
    /// <summary>
    /// 웨이포인트 추적 시스템 (서버 전용)
    /// - 현재 웨이포인트에 도착하면 다음 웨이포인트로 MoveTarget 업데이트
    /// - PathfindingSystem 이후, NetcodePlayerMovementSystem 이전에 실행
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PathfindingSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [BurstCompile]
    public partial struct WaypointFollowSystem : ISystem
    {
        private const float WaypointArrivalThreshold = 0.8f;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PathfindingState>();
        }

        public void OnUpdate(ref SystemState state)
        {
            foreach (var (pathState, pathBuffer, moveTarget, transform, entity) in
                SystemAPI.Query<RefRW<PathfindingState>, DynamicBuffer<PathWaypoint>, RefRW<MoveTarget>, RefRO<LocalTransform>>()
                    .WithAll<UnitTag>()
                    .WithEntityAccess())
            {
                // 유효한 경로가 없으면 스킵
                if (pathState.ValueRO.TotalWaypoints == 0 || !moveTarget.ValueRO.isValid)
                    continue;

                float3 currentPos = transform.ValueRO.Position;
                int currentIndex = pathState.ValueRO.CurrentWaypointIndex;

                // 버퍼 범위 체크
                if (currentIndex >= pathBuffer.Length)
                    continue;

                float3 currentWaypoint = pathBuffer[currentIndex].Position;

                // Y축 무시하고 거리 계산
                float3 toWaypoint = currentWaypoint - currentPos;
                toWaypoint.y = 0;
                float distance = math.length(toWaypoint);

                // 현재 웨이포인트에 도착했는지 확인
                if (distance < WaypointArrivalThreshold)
                {
                    int nextIndex = currentIndex + 1;

                    if (nextIndex < pathState.ValueRO.TotalWaypoints && nextIndex < pathBuffer.Length)
                    {
                        // 다음 웨이포인트로 이동
                        pathState.ValueRW.CurrentWaypointIndex = (byte)nextIndex;
                        moveTarget.ValueRW.position = pathBuffer[nextIndex].Position;
                    }
                    else
                    {
                        // 마지막 웨이포인트 도착 - 이동 완료
                        // MoveTarget.isValid는 NetcodePlayerMovementSystem에서 처리
                    }
                }
            }
        }
    }
}
