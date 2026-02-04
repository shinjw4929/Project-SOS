#pragma warning disable CS0618 // NavMeshQuery, NavMeshWorld, PolygonId - deprecated without replacement in Unity 6
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Experimental.AI;
using Shared;
using System.Diagnostics;
using PathQueryStatus = UnityEngine.Experimental.AI.PathQueryStatus;

namespace Server
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(NavMeshObstacleSpawnSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial struct PathfindingSystem : ISystem
    {
        // 성능 설정
        const int MaxPathLength = 64;
        const int PathNodePoolSize = 256;
        const int MaxIterationsPerQuery = 1024;
        const int MaxUpdateRetries = 4;
        const float MaxProcessingTimeMs = 1.0f;
        const float SampleExtent = 5.0f;

        // 시스템 상태
        NavMeshQuery _query;
        NativeHashMap<int, int> _agentIdCache;
        NativeArray<PolygonId> _polygonBuffer;
        NativeArray<float3> _waypointBuffer;
        int _initialSkipFrames;
        bool _queryCreated;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<MovementGoal>();

            _agentIdCache = new NativeHashMap<int, int>(4, Allocator.Persistent);
            _polygonBuffer = new NativeArray<PolygonId>(PathNodePoolSize, Allocator.Persistent);
            _waypointBuffer = new NativeArray<float3>(MaxPathLength, Allocator.Persistent);
            _initialSkipFrames = 10;
            _queryCreated = false;
        }

        public void OnDestroy(ref SystemState state)
        {
            if (_queryCreated)
                _query.Dispose();

            if (_agentIdCache.IsCreated)
                _agentIdCache.Dispose();

            if (_polygonBuffer.IsCreated)
                _polygonBuffer.Dispose();

            if (_waypointBuffer.IsCreated)
                _waypointBuffer.Dispose();
        }

        public void OnUpdate(ref SystemState state)
        {
            // 초기 안정화 대기
            if (_initialSkipFrames > 0)
            {
                _initialSkipFrames--;
                if (_initialSkipFrames == 0)
                    CacheAgentIDs();
                return;
            }

            // NavMeshQuery lazy 초기화
            if (!_queryCreated)
            {
                var world = NavMeshWorld.GetDefaultWorld();
                if (!world.IsValid())
                    return;

                _query = new NavMeshQuery(world, Allocator.Persistent, PathNodePoolSize);
                _queryCreated = true;
                CacheAgentIDs();
            }

            if (NavMesh.GetSettingsCount() == 0)
                return;

            // 타임 슬라이싱
            Stopwatch stopwatch = Stopwatch.StartNew();
            long maxTicks = (long)(Stopwatch.Frequency * (MaxProcessingTimeMs / 1000.0f));

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
                if (stopwatch.ElapsedTicks > maxTicks)
                    break;

                if (!goal.ValueRO.IsPathDirty)
                    continue;

                float3 startPos = transform.ValueRO.Position;
                float3 endPos = goal.ValueRO.Destination;

                if (!math.isfinite(startPos.x) || !math.isfinite(endPos.x))
                {
                    FailPath(pathBuffer, waypointsEnabled, ref goal.ValueRW);
                    continue;
                }

                // Agent ID 조회
                int agentIndex = agentConfig.ValueRO.AgentTypeIndex;
                if (!_agentIdCache.TryGetValue(agentIndex, out int agentTypeID))
                    agentTypeID = GetAgentTypeIDFromIndex(agentIndex);

                int waypointCount = CalculatePath(startPos, endPos, agentTypeID);

                if (waypointCount > 0)
                {
                    pathBuffer.Clear();
                    for (int i = 0; i < waypointCount; i++)
                    {
                        pathBuffer.Add(new PathWaypoint { Position = _waypointBuffer[i] });
                    }

                    ProcessFirstWaypoint(startPos, endPos, pathBuffer, waypoints, waypointsEnabled, goal);
                }
                else
                {
                    FailPath(pathBuffer, waypointsEnabled, ref goal.ValueRW);
                    continue;
                }

                goal.ValueRW.IsPathDirty = false;
            }
        }

        private int CalculatePath(float3 startPos, float3 endPos, int agentTypeID)
        {
            var extents = new float3(SampleExtent);

            // MapLocation (start)
            var startLoc = _query.MapLocation(startPos, extents, agentTypeID);
            if (!_query.IsValid(startLoc.polygon))
                return 0;

            // MapLocation (end)
            var endLoc = _query.MapLocation(endPos, extents, agentTypeID);
            if (!_query.IsValid(endLoc.polygon))
                return 0;

            // BeginFindPath
            var status = _query.BeginFindPath(startLoc, endLoc);
            if ((status & PathQueryStatus.Failure) != 0)
                return 0;

            // UpdateFindPath (반복)
            int retries = 0;
            while ((status & PathQueryStatus.InProgress) != 0 && retries < MaxUpdateRetries)
            {
                status = _query.UpdateFindPath(MaxIterationsPerQuery, out _);
                retries++;
            }

            if ((status & PathQueryStatus.Success) == 0)
                return 0;

            // EndFindPath (Success 비트가 설정되면 partial path도 포함)
            status = _query.EndFindPath(out int pathLength);
            if ((status & PathQueryStatus.Success) == 0)
                return 0;

            pathLength = math.min(pathLength, _polygonBuffer.Length);

            // GetPathResult
            _query.GetPathResult(_polygonBuffer);

            // Funnel 알고리즘으로 직선 웨이포인트 변환
            // endPos 원본 사용: MapLocation 스냅 위치(endLoc.position)를 쓰면
            // 건설 도착 판정 등에서 목표 좌표 불일치 발생
            int waypointCount = NavMeshPathUtils.FindStraightPath(
                ref _query,
                _polygonBuffer,
                pathLength,
                startLoc.position,
                endPos,
                _waypointBuffer,
                MaxPathLength);

            return waypointCount;
        }

        private static void FailPath(
            DynamicBuffer<PathWaypoint> pathBuffer,
            EnabledRefRW<MovementWaypoints> waypointsEnabled,
            ref MovementGoal goal)
        {
            pathBuffer.Clear();
            waypointsEnabled.ValueRW = false;
            goal.IsPathDirty = false;
        }

        private static void ProcessFirstWaypoint(
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

                if (distToGoalSq > 0.01f)
                {
                    float3 goalDir = toGoal * math.rsqrt(distToGoalSq);

                    while (firstTargetIndex < pathBuffer.Length - 1)
                    {
                        float3 targetPos = pathBuffer[firstTargetIndex].Position;
                        float3 toWaypoint = targetPos - currentPos;
                        float distToWaypointSq = math.lengthsq(toWaypoint);

                        if (distToWaypointSq < 0.25f)
                        {
                            firstTargetIndex++;
                            continue;
                        }

                        float3 waypointDir = toWaypoint * math.rsqrt(distToWaypointSq);
                        if (math.dot(goalDir, waypointDir) > 0.2f)
                            break;

                        firstTargetIndex++;
                    }
                }

                goal.ValueRW.CurrentWaypointIndex = (byte)firstTargetIndex;
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
            int count = NavMesh.GetSettingsCount();
            if (index >= 0 && index < count)
                return NavMesh.GetSettingsByIndex(index).agentTypeID;
            return count > 0 ? NavMesh.GetSettingsByIndex(0).agentTypeID : 0;
        }
    }
}
