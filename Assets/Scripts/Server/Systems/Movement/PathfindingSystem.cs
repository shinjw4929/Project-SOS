using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine.AI;
using UnityEngine.Experimental.AI;
using Shared;

namespace Server
{
    /// <summary>
    /// NavMesh 기반 경로 계산 시스템 (서버 전용)
    /// - PathfindingState.NeedsPath가 true인 유닛의 경로 계산
    /// - 프레임당 최대 10개 처리 (부하 분산)
    /// - 결과를 PathWaypoint 버퍼에 저장
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial class PathfindingSystem : SystemBase
    {
        private const int MaxPathRequestsPerFrame = 10;
        private const int MaxPathLength = 128;
        private const float PathNodeDistance = 1.0f; // 경로 노드 간 거리

        protected override void OnCreate()
        {
            RequireForUpdate<PathfindingState>();
        }

        protected override void OnUpdate()
        {
            int processedCount = 0;

            // NavMesh가 준비되었는지 확인
            if (!NavMesh.SamplePosition(float3.zero, out _, 1f, NavMesh.AllAreas))
            {
                // NavMesh가 아직 로드되지 않음
                return;
            }

            foreach (var (pathState, pathBuffer, moveTarget, transform, entity) in
                SystemAPI.Query<RefRW<PathfindingState>, DynamicBuffer<PathWaypoint>, RefRW<MoveTarget>, RefRO<LocalTransform>>()
                    .WithAll<UnitTag>()
                    .WithEntityAccess())
            {
                if (!pathState.ValueRO.NeedsPath)
                    continue;

                if (processedCount >= MaxPathRequestsPerFrame)
                    break;

                float3 startPos = transform.ValueRO.Position;
                float3 endPos = pathState.ValueRO.FinalDestination;

                // 경로 계산
                bool pathFound = CalculatePath(startPos, endPos, pathBuffer);

                if (pathFound && pathBuffer.Length > 0)
                {
                    pathState.ValueRW.TotalWaypoints = (byte)math.min(pathBuffer.Length, 255);
                    pathState.ValueRW.CurrentWaypointIndex = 0;

                    // 첫 번째 웨이포인트를 MoveTarget에 설정
                    moveTarget.ValueRW.position = pathBuffer[0].Position;
                    moveTarget.ValueRW.isValid = true;
                }
                else
                {
                    // 경로를 찾을 수 없으면 직선 이동
                    pathBuffer.Clear();
                    pathBuffer.Add(new PathWaypoint { Position = endPos });
                    pathState.ValueRW.TotalWaypoints = 1;
                    pathState.ValueRW.CurrentWaypointIndex = 0;

                    moveTarget.ValueRW.position = endPos;
                    moveTarget.ValueRW.isValid = true;
                }

                pathState.ValueRW.NeedsPath = false;
                processedCount++;
            }
        }

        /// <summary>
        /// NavMesh 경로 계산
        /// </summary>
        private bool CalculatePath(float3 start, float3 end, DynamicBuffer<PathWaypoint> pathBuffer)
        {
            pathBuffer.Clear();

            // 좌표 유효성 검사 (NaN, Infinity 방지)
            if (!math.isfinite(start.x) || !math.isfinite(start.y) || !math.isfinite(start.z) ||
                !math.isfinite(end.x) || !math.isfinite(end.y) || !math.isfinite(end.z))
            {
                return false;
            }

            //            // 시작점이 NavMesh 위에 있는지 확인
            if (!NavMesh.SamplePosition(start, out NavMeshHit startHit, 2f, NavMesh.AllAreas))
                return false;

            // 끝점이 NavMesh 위에 있는지 확인
            if (!NavMesh.SamplePosition(end, out NavMeshHit endHit, 2f, NavMesh.AllAreas))
                return false;

            // NavMeshPath 계산
            NavMeshPath path = new NavMeshPath();
            if (!NavMesh.CalculatePath(startHit.position, endHit.position, NavMesh.AllAreas, path))
                return false;

            if (path.status == NavMeshPathStatus.PathInvalid)
                return false;

            // 경로를 PathWaypoint 버퍼에 저장
            var corners = path.corners;
            for (int i = 0; i < corners.Length && i < MaxPathLength; i++)
            {
                pathBuffer.Add(new PathWaypoint
                {
                    Position = corners[i]
                });
            }

            return pathBuffer.Length > 0;
        }
    }
}
