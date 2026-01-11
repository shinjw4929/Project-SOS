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
    [UpdateAfter(typeof(NavMeshObstacleSpawnSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial class PathfindingSystem : SystemBase
    {
        private const int MaxPathRequestsPerFrame = 15;
        private const int MaxPathLength = 64;

        // 초기 프레임 스킵: NavMesh 장애물(벽) 업데이트 완료 대기
        private int _initialSkipFrames; 
        
        protected override void OnCreate()
        {
            RequireForUpdate<MovementGoal>();
            _initialSkipFrames = 10; // NavMesh carving 완료 대기 (약 0.16초 @ 60fps)
        }

        protected override void OnUpdate()
        {
            // NavMesh 장애물 업데이트 완료 대기 (초기 프레임 스킵)
            if (_initialSkipFrames > 0)
            {
                _initialSkipFrames--;
                return;
            }

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
                    .WithAny<UnitTag, EnemyTag>()
                    .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
            {
                if (!goal.ValueRO.IsPathDirty)
                    continue;

                if (processedCount >= MaxPathRequestsPerFrame)
                    break;

                // 1. 경로 계산
                float3 startPos = transform.ValueRO.Position;
                float3 endPos = goal.ValueRO.Destination;

                bool pathValid = CalculatePath(startPos, endPos, pathBuffer);

                // 경로 계산 실패 (NavMesh 업데이트 대기 중) - 다음 프레임에 재시도
                if (!pathValid)
                {
                    // IsPathDirty는 true로 유지되어 다음 프레임에 재시도됨
                    processedCount++;  // 무한 루프 방지를 위해 카운트 증가
                    continue;
                }

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

        /// <summary>
        /// NavMesh 경로 계산
        /// </summary>
        /// <returns>true: 완전한 경로 계산 성공, false: 재시도 필요 (NavMesh 업데이트 대기)</returns>
        private bool CalculatePath(float3 start, float3 end, DynamicBuffer<PathWaypoint> pathBuffer)
        {
            pathBuffer.Clear();

            if (!math.isfinite(start.x) || !math.isfinite(end.x)) return false;

            // NavMesh 샘플링 (Vector3 변환 필요)
            NavMeshHit hit;
            if (!NavMesh.SamplePosition(start, out hit, 5.0f, NavMesh.AllAreas)) return false;
            Vector3 startPoint = hit.position;

            if (!NavMesh.SamplePosition(end, out hit, 5.0f, NavMesh.AllAreas)) return false;
            Vector3 endPoint = hit.position;

            NavMeshPath path = new NavMeshPath();
            if (NavMesh.CalculatePath(startPoint, endPoint, NavMesh.AllAreas, path))
            {
                // PathComplete 또는 PathPartial 모두 허용
                // PathPartial: 목적지까지 완전한 경로는 없지만, 갈 수 있는 만큼 이동
                if (path.status == NavMeshPathStatus.PathComplete ||
                    path.status == NavMeshPathStatus.PathPartial)
                {
                    var corners = path.corners;
                    int count = math.min(corners.Length, MaxPathLength);
                    for (int i = 0; i < count; i++)
                    {
                        pathBuffer.Add(new PathWaypoint { Position = corners[i] });
                    }
                    return true;
                }
            }

            // PathInvalid 또는 CalculatePath 실패 - 재시도
            return false;
        }
    }
}