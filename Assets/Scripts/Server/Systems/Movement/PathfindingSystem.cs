#pragma warning disable CS0618 // NavMeshQuery, NavMeshWorld, PolygonId - deprecated without replacement in Unity 6
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Experimental.AI;
using Shared;
using PathQueryStatus = UnityEngine.Experimental.AI.PathQueryStatus;

namespace Server
{
    struct PathRequest
    {
        public Entity Entity;
        public float3 StartPos;
        public float3 EndPos;
        public int AgentTypeID; // -1 = invalid (NaN 좌표 등 → Apply에서 FailPath 처리)
    }

    struct PathOutput
    {
        public int WaypointCount; // 0 = 경로 계산 실패
        public bool IsPartial;
    }

    // NavMeshQuery는 managed 내부 호출 → Burst 불가
    struct PathComputeJob : IJob
    {
        public int StartIndex;
        public int EndIndex;

        [ReadOnly, NativeDisableContainerSafetyRestriction]
        public NativeArray<PathRequest> Requests;

        // 워커 전용 (struct 복사, IntPtr 동일 → 같은 네이티브 핸들)
        public NavMeshQuery Query;
        [NativeDisableContainerSafetyRestriction]
        public NativeArray<PolygonId> PolygonBuffer;
        [NativeDisableContainerSafetyRestriction]
        public NativeArray<float3> TempWaypointBuffer;

        // 공유 출력 (비겹침 인덱스 범위 쓰기)
        [NativeDisableContainerSafetyRestriction]
        public NativeArray<PathOutput> Outputs;
        [NativeDisableContainerSafetyRestriction]
        public NativeArray<float3> OutputWaypoints;

        const int MaxIterationsPerQuery = 1024;
        const int MaxUpdateRetries = 4;
        const int MaxPathLength = 64;
        const float SampleExtent = 5.0f;

        public void Execute()
        {
            for (int i = StartIndex; i < EndIndex; i++)
            {
                var req = Requests[i];
                if (req.AgentTypeID < 0)
                {
                    Outputs[i] = default;
                    continue;
                }

                var result = CalculatePath(req.StartPos, req.EndPos, req.AgentTypeID, i);
                Outputs[i] = result;
            }
        }

        PathOutput CalculatePath(float3 startPos, float3 endPos, int agentTypeID, int requestIndex)
        {
            var fail = new PathOutput { WaypointCount = 0, IsPartial = false };
            var extents = new float3(SampleExtent);

            var startLoc = Query.MapLocation(startPos, extents, agentTypeID);
            if (!Query.IsValid(startLoc.polygon))
                return fail;

            var endLoc = Query.MapLocation(endPos, extents, agentTypeID);
            if (!Query.IsValid(endLoc.polygon))
                return fail;

            var status = Query.BeginFindPath(startLoc, endLoc);
            if ((status & PathQueryStatus.Failure) != 0)
                return fail;

            int retries = 0;
            while ((status & PathQueryStatus.InProgress) != 0 && retries < MaxUpdateRetries)
            {
                status = Query.UpdateFindPath(MaxIterationsPerQuery, out _);
                retries++;
            }

            if ((status & PathQueryStatus.Success) == 0)
                return fail;

            status = Query.EndFindPath(out int pathLength);
            if ((status & PathQueryStatus.Success) == 0)
                return fail;

            pathLength = math.min(pathLength, PolygonBuffer.Length);

            Query.GetPathResult(PolygonBuffer);

            bool isPartial = pathLength > 0 && PolygonBuffer[pathLength - 1] != endLoc.polygon;

            int waypointCount = NavMeshPathUtils.FindStraightPath(
                ref Query,
                PolygonBuffer,
                pathLength,
                startLoc.position,
                endPos,
                TempWaypointBuffer,
                MaxPathLength);

            if (isPartial && waypointCount > 2)
                waypointCount--;

            int offset = requestIndex * MaxPathLength;
            for (int w = 0; w < waypointCount; w++)
                OutputWaypoints[offset + w] = TempWaypointBuffer[w];

            return new PathOutput { WaypointCount = waypointCount, IsPartial = isPartial };
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(NavMeshObstacleSpawnSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial struct PathfindingSystem : ISystem
    {
        const int MaxPathLength = 64;
        const int PathNodePoolSize = 256;
        const int WorkerCount = 8;
        const int InitialRequestCapacity = 1024;

        // 병렬 워커 리소스 (Persistent)
        NativeArray<NavMeshQuery> _queries;
        NativeArray<PolygonId> _polygonBuffers;       // WorkerCount * PathNodePoolSize
        NativeArray<float3> _tempWaypointBuffers;     // WorkerCount * MaxPathLength

        // 요청/결과 버퍼 (Persistent, 동적 확장)
        NativeArray<PathRequest> _requestArray;
        NativeArray<PathOutput> _outputs;
        NativeArray<float3> _outputWaypoints;

        NativeHashMap<int, int> _agentIdCache;
        int _initialSkipFrames;
        bool _queriesCreated;
        int _requestCount;
        int _requestCapacity;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<MovementGoal>();

            _queries = new NativeArray<NavMeshQuery>(WorkerCount, Allocator.Persistent);
            _polygonBuffers = new NativeArray<PolygonId>(WorkerCount * PathNodePoolSize, Allocator.Persistent);
            _tempWaypointBuffers = new NativeArray<float3>(WorkerCount * MaxPathLength, Allocator.Persistent);
            _requestCapacity = InitialRequestCapacity;
            _requestArray = new NativeArray<PathRequest>(_requestCapacity, Allocator.Persistent);
            _outputs = new NativeArray<PathOutput>(_requestCapacity, Allocator.Persistent);
            _outputWaypoints = new NativeArray<float3>(_requestCapacity * MaxPathLength, Allocator.Persistent);
            _agentIdCache = new NativeHashMap<int, int>(4, Allocator.Persistent);

            _initialSkipFrames = 10;
            _queriesCreated = false;
        }

        public void OnDestroy(ref SystemState state)
        {
            if (_queriesCreated)
            {
                for (int i = 0; i < WorkerCount; i++)
                    _queries[i].Dispose();
            }

            if (_queries.IsCreated) _queries.Dispose();
            if (_polygonBuffers.IsCreated) _polygonBuffers.Dispose();
            if (_tempWaypointBuffers.IsCreated) _tempWaypointBuffers.Dispose();
            if (_requestArray.IsCreated) _requestArray.Dispose();
            if (_outputs.IsCreated) _outputs.Dispose();
            if (_outputWaypoints.IsCreated) _outputWaypoints.Dispose();
            if (_agentIdCache.IsCreated) _agentIdCache.Dispose();
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

            // NavMeshQuery lazy 초기화 (WorkerCount개)
            if (!_queriesCreated)
            {
                var world = NavMeshWorld.GetDefaultWorld();
                if (!world.IsValid())
                    return;

                for (int i = 0; i < WorkerCount; i++)
                    _queries[i] = new NavMeshQuery(world, Allocator.Persistent, PathNodePoolSize);

                _queriesCreated = true;
                CacheAgentIDs();
            }

            if (NavMesh.GetSettingsCount() == 0)
                return;

            // ── Phase 1: Collect (메인 스레드) ──
            _requestCount = 0;

            foreach (var (goal, transform, agentConfig, entity) in
                SystemAPI.Query<
                    RefRO<MovementGoal>,
                    RefRO<LocalTransform>,
                    RefRO<NavMeshAgentConfig>>()
                    .WithAny<UnitTag, EnemyTag>()
                    .WithEntityAccess())
            {
                if (!goal.ValueRO.IsPathDirty)
                    continue;

                if (_requestCount >= _requestCapacity)
                    GrowRequestBuffers();

                float3 startPos = transform.ValueRO.Position;
                float3 endPos = goal.ValueRO.Destination;

                if (!math.isfinite(startPos.x) || !math.isfinite(endPos.x))
                {
                    _requestArray[_requestCount] = new PathRequest
                    {
                        Entity = entity,
                        StartPos = startPos,
                        EndPos = endPos,
                        AgentTypeID = -1 // sentinel → Apply에서 FailPath
                    };
                    _requestCount++;
                    continue;
                }

                int agentIndex = agentConfig.ValueRO.AgentTypeIndex;
                if (!_agentIdCache.TryGetValue(agentIndex, out int agentTypeID))
                    agentTypeID = GetAgentTypeIDFromIndex(agentIndex);

                _requestArray[_requestCount] = new PathRequest
                {
                    Entity = entity,
                    StartPos = startPos,
                    EndPos = endPos,
                    AgentTypeID = agentTypeID
                };
                _requestCount++;
            }

            if (_requestCount == 0)
                return;

            // ── Phase 2: Compute (N개 IJob 병렬) ──
            int batchSize = (_requestCount + WorkerCount - 1) / WorkerCount;
            var handles = new NativeArray<JobHandle>(WorkerCount, Allocator.Temp);
            int scheduledCount = 0;

            for (int w = 0; w < WorkerCount; w++)
            {
                int start = w * batchSize;
                if (start >= _requestCount)
                    break;
                int end = math.min(start + batchSize, _requestCount);

                handles[scheduledCount] = new PathComputeJob
                {
                    StartIndex = start,
                    EndIndex = end,
                    Requests = _requestArray,
                    Query = _queries[w],
                    PolygonBuffer = _polygonBuffers.GetSubArray(w * PathNodePoolSize, PathNodePoolSize),
                    TempWaypointBuffer = _tempWaypointBuffers.GetSubArray(w * MaxPathLength, MaxPathLength),
                    Outputs = _outputs,
                    OutputWaypoints = _outputWaypoints,
                }.Schedule();
                scheduledCount++;
            }

            var combined = JobHandle.CombineDependencies(handles.GetSubArray(0, scheduledCount));
            combined.Complete();
            handles.Dispose();

            // ── Phase 3: Apply (메인 스레드) ──
            var pathBufferLookup = SystemAPI.GetBufferLookup<PathWaypoint>();
            var goalLookup = SystemAPI.GetComponentLookup<MovementGoal>();
            var waypointsLookup = SystemAPI.GetComponentLookup<MovementWaypoints>();
            var transformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true);

            for (int i = 0; i < _requestCount; i++)
            {
                Entity entity = _requestArray[i].Entity;
                if (!goalLookup.HasComponent(entity))
                    continue; // 프레임 간 파괴 대응

                var output = _outputs[i];

                if (output.WaypointCount <= 0 || _requestArray[i].AgentTypeID < 0)
                {
                    pathBufferLookup[entity].Clear();
                    waypointsLookup.SetComponentEnabled(entity, false);
                    var failGoal = goalLookup[entity];
                    failGoal.IsPathDirty = false;
                    failGoal.IsPathPartial = output.WaypointCount == 0 || _requestArray[i].AgentTypeID < 0;
                    goalLookup[entity] = failGoal;
                    continue;
                }

                var buffer = pathBufferLookup[entity];
                buffer.Clear();
                int offset = i * MaxPathLength;
                for (int w = 0; w < output.WaypointCount; w++)
                    buffer.Add(new PathWaypoint { Position = _outputWaypoints[offset + w] });

                var waypoints = waypointsLookup[entity];
                var goal = goalLookup[entity];
                float3 currentPos = transformLookup[entity].Position;
                bool enabled = ProcessFirstWaypoint(currentPos, _requestArray[i].EndPos, buffer, ref waypoints, ref goal);

                goal.IsPathPartial = output.IsPartial;
                goal.IsPathDirty = false;
                goalLookup[entity] = goal;
                waypointsLookup[entity] = waypoints;
                waypointsLookup.SetComponentEnabled(entity, enabled);
            }
        }

        static bool ProcessFirstWaypoint(
            float3 currentPos,
            float3 goalPos,
            DynamicBuffer<PathWaypoint> pathBuffer,
            ref MovementWaypoints waypoints,
            ref MovementGoal goal)
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

                goal.CurrentWaypointIndex = (byte)firstTargetIndex;
                waypoints.Current = pathBuffer[firstTargetIndex].Position;

                if (pathBuffer.Length > firstTargetIndex + 1)
                {
                    waypoints.Next = pathBuffer[firstTargetIndex + 1].Position;
                    waypoints.HasNext = true;
                }
                else
                {
                    waypoints.HasNext = false;
                }

                return true;
            }

            return false;
        }

        void GrowRequestBuffers()
        {
            int newCapacity = _requestCapacity * 2;

            var newRequests = new NativeArray<PathRequest>(newCapacity, Allocator.Persistent);
            NativeArray<PathRequest>.Copy(_requestArray, newRequests, _requestCount);
            _requestArray.Dispose();
            _requestArray = newRequests;

            _outputs.Dispose();
            _outputs = new NativeArray<PathOutput>(newCapacity, Allocator.Persistent);

            _outputWaypoints.Dispose();
            _outputWaypoints = new NativeArray<float3>(newCapacity * MaxPathLength, Allocator.Persistent);

            _requestCapacity = newCapacity;
        }

        void CacheAgentIDs()
        {
            _agentIdCache.Clear();
            int count = NavMesh.GetSettingsCount();
            for (int i = 0; i < count; i++)
            {
                var settings = NavMesh.GetSettingsByIndex(i);
                _agentIdCache.TryAdd(i, settings.agentTypeID);
            }
        }

        int GetAgentTypeIDFromIndex(int index)
        {
            int count = NavMesh.GetSettingsCount();
            if (index >= 0 && index < count)
                return NavMesh.GetSettingsByIndex(index).agentTypeID;
            return count > 0 ? NavMesh.GetSettingsByIndex(0).agentTypeID : 0;
        }
    }
}
