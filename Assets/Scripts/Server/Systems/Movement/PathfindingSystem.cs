using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.AI;
using Shared;

namespace Server
{
    /// <summary>
    /// NavMesh 경로 계산 시스템 (Server Only)
    /// - MovementGoal.IsPathDirty == true일 때 경로를 계산하고 버퍼에 담음
    /// - 계산 직후 첫 번째 웨이포인트를 MovementWaypoints에 주입하여 즉시 이동 시작
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial class PathfindingSystem : SystemBase
    {
        private const int MaxPathRequestsPerFrame = 15; 
        private const int MaxPathLength = 64; 
        
        protected override void OnCreate()
        {
            RequireForUpdate<MovementGoal>();
        }

        protected override void OnUpdate()
        {
            if (NavMesh.GetSettingsCount() == 0) return;

            int processedCount = 0;

            // [중요] IgnoreComponentEnabledState 추가하여 비활성화 상태의 MovementWaypoints도 쿼리
            foreach (var (goal, pathBuffer, waypoints, waypointsEnabled, transform) in
                SystemAPI.Query<
                    RefRW<MovementGoal>,
                    DynamicBuffer<PathWaypoint>,
                    RefRW<MovementWaypoints>,
                    EnabledRefRW<MovementWaypoints>,
                    RefRO<LocalTransform>>()
                    .WithAll<UnitTag>()
                    .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
            {
                if (!goal.ValueRO.IsPathDirty)
                    continue;

                if (processedCount >= MaxPathRequestsPerFrame)
                    break;

                // 1. 경로 계산
                float3 startPos = transform.ValueRO.Position;
                float3 endPos = goal.ValueRO.Destination;

                CalculatePath(startPos, endPos, pathBuffer);

                // 2. 경로 계산 후 처리
                // NavMesh 경로의 0번은 항상 '현재 위치'이므로, 실제 목표는 1번부터임
                if (pathBuffer.Length > 1)
                {
                    int firstTargetIndex = 1;

                    // Goal 상태 업데이트
                    goal.ValueRW.CurrentWaypointIndex = (byte)firstTargetIndex;

                    // 즉시 이동 시작을 위해 MovementWaypoints 초기화
                    waypoints.ValueRW.Current = pathBuffer[firstTargetIndex].Position;

                    // 다음 웨이포인트가 있다면 미리 세팅 (코너링용)
                    if (pathBuffer.Length > firstTargetIndex + 1)
                    {
                        waypoints.ValueRW.Next = pathBuffer[firstTargetIndex + 1].Position;
                        waypoints.ValueRW.HasNext = true;
                    }
                    else
                    {
                        waypoints.ValueRW.HasNext = false;
                    }

                    // 컴포넌트 활성화 -> PredictedMovementSystem이 작동 시작
                    waypointsEnabled.ValueRW = true;
                }
                else
                {
                    // 경로가 없거나 제자리인 경우
                    pathBuffer.Clear();
                    waypointsEnabled.ValueRW = false; // 이동 정지
                }

                goal.ValueRW.IsPathDirty = false;
                processedCount++;
            }
        }

        private void CalculatePath(float3 start, float3 end, DynamicBuffer<PathWaypoint> pathBuffer)
        {
            pathBuffer.Clear();

            if (!math.isfinite(start.x) || !math.isfinite(end.x)) return;

            // NavMesh 샘플링 (Vector3 변환 필요)
            NavMeshHit hit;
            if (!NavMesh.SamplePosition(start, out hit, 5.0f, NavMesh.AllAreas)) return;
            Vector3 startPoint = hit.position;

            if (!NavMesh.SamplePosition(end, out hit, 5.0f, NavMesh.AllAreas)) return;
            Vector3 endPoint = hit.position;

            NavMeshPath path = new NavMeshPath();
            if (NavMesh.CalculatePath(startPoint, endPoint, NavMesh.AllAreas, path) && 
                path.status != NavMeshPathStatus.PathInvalid)
            {
                var corners = path.corners;
                int count = math.min(corners.Length, MaxPathLength);
                for (int i = 0; i < count; i++)
                {
                    pathBuffer.Add(new PathWaypoint { Position = corners[i] });
                }
            }
            else
            {
                // 실패 시 직선 경로 Fallback (0: 시작점, 1: 끝점)
                pathBuffer.Add(new PathWaypoint { Position = start });
                pathBuffer.Add(new PathWaypoint { Position = end });
            }
        }
    }
}