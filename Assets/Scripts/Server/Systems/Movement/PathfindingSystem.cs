using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.AI;
using Shared;
using System.Diagnostics; // Stopwatch 사용

namespace Server
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(NavMeshObstacleSpawnSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial class PathfindingSystem : SystemBase
    {
        // 성능 설정
        private const float MaxProcessingTimeMs = 1.0f; // 프레임당 최대 경로 계산 시간 (밀리초)
        private const int MaxPathLength = 64;

        // 재사용 객체 (GC 방지)
        private NavMeshPath _sharedNavMeshPath;
        private Vector3[] _sharedCornerBuffer;
        private NavMeshQueryFilter _sharedFilter;

        // 캐싱 데이터
        private NativeHashMap<int, int> _agentIdCache;
        private int _initialSkipFrames;

        protected override void OnCreate()
        {
            RequireForUpdate<MovementGoal>();
            
            // 재사용을 위한 객체 초기화
            _sharedNavMeshPath = new NavMeshPath();
            _sharedCornerBuffer = new Vector3[MaxPathLength];
            _sharedFilter = new NavMeshQueryFilter { areaMask = NavMesh.AllAreas };
            _agentIdCache = new NativeHashMap<int, int>(4, Allocator.Persistent);

            _initialSkipFrames = 10;
            
            // Agent ID 캐싱
            CacheAgentIDs();
        }

        protected override void OnDestroy()
        {
            if (_agentIdCache.IsCreated)
                _agentIdCache.Dispose();
        }

        protected override void OnUpdate()
        {
            // 초기 안정화 대기
            if (_initialSkipFrames > 0)
            {
                _initialSkipFrames--;
                if (_initialSkipFrames == 0) CacheAgentIDs(); // 런타임에 세팅이 로드될 경우를 대비해 한 번 더 캐싱
                return;
            }

            if (NavMesh.GetSettingsCount() == 0) return;

            // 타임 슬라이싱을 위한 타이머 시작
            Stopwatch stopwatch = Stopwatch.StartNew();
            long maxTicks = (long)(Stopwatch.Frequency * (MaxProcessingTimeMs / 1000.0f));

            // Entities 순회
            foreach (var (goal, pathBuffer, waypoints, waypointsEnabled, transform, agentConfig) in
                SystemAPI.Query<
                    RefRW<MovementGoal>,
                    DynamicBuffer<PathWaypoint>,
                    RefRW<MovementWaypoints>,
                    EnabledRefRW<MovementWaypoints>,
                    RefRO<LocalTransform>,
                    RefRO<NavMeshAgentConfig>>()
                    .WithAny<UnitTag, EnemyTag>()
                    .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
            {
                // 처리 시간 제한 초과 시 다음 프레임으로 미룸
                if (stopwatch.ElapsedTicks > maxTicks)
                    break;

                if (!goal.ValueRO.IsPathDirty)
                    continue;

                float3 startPos = transform.ValueRO.Position;
                float3 endPos = goal.ValueRO.Destination;

                // 캐싱된 Agent ID 조회
                int agentIndex = agentConfig.ValueRO.AgentTypeIndex;
                if (!_agentIdCache.TryGetValue(agentIndex, out int agentTypeID))
                {
                    // 캐시에 없으면 폴백 및 재캐싱 시도
                    agentTypeID = GetAgentTypeIDFromIndex(agentIndex);
                }

                // 경로 계산 수행
                // GC를 발생시키지 않는 최적화된 메서드 사용
                int cornersCount = CalculatePathNonAlloc(startPos, endPos, agentTypeID);

                if (cornersCount > 0)
                {
                    // 경로 버퍼 업데이트
                    pathBuffer.Clear();
                    for (int i = 0; i < cornersCount; i++)
                    {
                        pathBuffer.Add(new PathWaypoint { Position = _sharedCornerBuffer[i] });
                    }

                    // 웨이포인트 후처리 (Next Waypoint 선택 로직)
                    ProcessFirstWaypoint(startPos, endPos, pathBuffer, waypoints, waypointsEnabled, goal);
                }
                else
                {
                    // 경로 계산 실패 또는 유효하지 않음 (IsPathDirty 유지 또는 리셋 정책 결정 필요)
                    // 여기서는 실패 시 일단 정지 처리하고 Dirty를 끔 (무한 재계산 방지)
                    pathBuffer.Clear();
                    waypointsEnabled.ValueRW = false;
                }

                // 처리 완료 플래그
                goal.ValueRW.IsPathDirty = false;
            }
        }

        /// <summary>
        /// NonAlloc 버전의 경로 계산 함수
        /// </summary>
        private int CalculatePathNonAlloc(float3 start, float3 end, int agentTypeID)
        {
            if (!math.isfinite(start.x) || !math.isfinite(end.x)) return 0;

            _sharedNavMeshPath.ClearCorners();
            _sharedFilter.agentTypeID = agentTypeID;

            // 1. Sample Position (Start)
            if (!NavMesh.SamplePosition(start, out NavMeshHit startHit, 5.0f, _sharedFilter))
                return 0;

            // 2. Sample Position (End)
            if (!NavMesh.SamplePosition(end, out NavMeshHit endHit, 5.0f, _sharedFilter))
                return 0;

            // 3. Calculate Path
            if (NavMesh.CalculatePath(startHit.position, endHit.position, _sharedFilter, _sharedNavMeshPath))
            {
                if (_sharedNavMeshPath.status == NavMeshPathStatus.PathComplete ||
                    _sharedNavMeshPath.status == NavMeshPathStatus.PathPartial)
                {
                    int count = _sharedNavMeshPath.GetCornersNonAlloc(_sharedCornerBuffer);
                    return math.min(count, MaxPathLength);
                }
            }

            return 0;
        }

        private void ProcessFirstWaypoint(
            float3 currentPos, 
            float3 goalPos,
            DynamicBuffer<PathWaypoint> pathBuffer,
            RefRW<MovementWaypoints> waypoints,
            EnabledRefRW<MovementWaypoints> waypointsEnabled,
            RefRW<MovementGoal> goal)
        {
            if (pathBuffer.Length > 1)
            {
                int firstTargetIndex = 1;
                float3 toGoal = goalPos - currentPos;
                float distToGoalSq = math.lengthsq(toGoal);

                // 목표가 충분히 멀리 있고 예측 이동이 필요한 경우
                if (distToGoalSq > 0.01f) // 0.1f * 0.1f
                {
                    float3 goalDir = toGoal * math.rsqrt(distToGoalSq); // Normalize

                    // "Look-ahead" 로직: 지나친 웨이포인트 스킵
                    // 루프 내에서 불필요한 length 호출을 줄이기 위해 lengthsq 사용 권장
                    while (firstTargetIndex < pathBuffer.Length - 1)
                    {
                        float3 targetPos = pathBuffer[firstTargetIndex].Position;
                        float3 toWaypoint = targetPos - currentPos;
                        float distToWaypointSq = math.lengthsq(toWaypoint);

                        // 너무 가까우면 스킵 (0.5m^2 = 0.25)
                        if (distToWaypointSq < 0.25f)
                        {
                            firstTargetIndex++;
                            continue;
                        }

                        // 방향 검사 (Dot Product)
                        float3 waypointDir = toWaypoint * math.rsqrt(distToWaypointSq);
                        if (math.dot(goalDir, waypointDir) > 0.2f) 
                            break;

                        firstTargetIndex++;
                    }
                }

                // Goal Index 업데이트
                goal.ValueRW.CurrentWaypointIndex = (byte)firstTargetIndex;

                // 이동 컴포넌트 데이터 주입
                waypoints.ValueRW.Current = pathBuffer[firstTargetIndex].Position;

                if (pathBuffer.Length > firstTargetIndex + 1)
                {
                    waypoints.ValueRW.Next = pathBuffer[firstTargetIndex + 1].Position;
                    waypoints.ValueRW.HasNext = true;
                }
                else
                {
                    waypoints.ValueRW.HasNext = false;
                }

                waypointsEnabled.ValueRW = true;
            }
            else
            {
                waypointsEnabled.ValueRW = false;
            }
        }

        private void CacheAgentIDs()
        {
            _agentIdCache.Clear();
            int count = NavMesh.GetSettingsCount();
            for (int i = 0; i < count; i++)
            {
                var settings = NavMesh.GetSettingsByIndex(i);
                _agentIdCache.TryAdd(i, settings.agentTypeID);
            }
        }

        private int GetAgentTypeIDFromIndex(int index)
        {
            // 폴백 메서드 (캐시에 없을 때만 호출됨)
            int count = NavMesh.GetSettingsCount();
            if (index >= 0 && index < count)
                return NavMesh.GetSettingsByIndex(index).agentTypeID;
            return count > 0 ? NavMesh.GetSettingsByIndex(0).agentTypeID : 0;
        }
    }
}