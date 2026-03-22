using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using Shared;

namespace Server
{
    struct PendingFlowFieldUnit
    {
        public Entity Entity;
        public int DestinationKey;
        public int2 CurrentCell;
    }

    [BurstCompile]
    struct FlowFieldComputeJob : IJob
    {
        public int StartIndex;
        public int EndIndex;

        [ReadOnly] public NativeArray<int2> Destinations;
        [ReadOnly] public NativeArray<int> PoolIndices;
        [ReadOnly] public NativeArray<byte> PassabilityMap;
        public int2 GridSize;
        public int GridCellCount;

        // Per-worker buffers (subarray from flat storage)
        [NativeDisableContainerSafetyRestriction]
        public NativeArray<int2> BfsQueue;
        [NativeDisableContainerSafetyRestriction]
        public NativeArray<byte> Visited;
        [NativeDisableContainerSafetyRestriction]
        public NativeArray<ushort> CostMap;

        // Output field pool (write to subarray per destination)
        [NativeDisableContainerSafetyRestriction]
        public NativeArray<byte> FieldPool;

        public void Execute()
        {
            for (int i = StartIndex; i < EndIndex; i++)
            {
                int poolIndex = PoolIndices[i];
                var outputField = FieldPool.GetSubArray(poolIndex * GridCellCount, GridCellCount);

                // 워커 메모리 재초기화
                unsafe
                {
                    UnsafeUtility.MemSet(outputField.GetUnsafePtr(), 255, GridCellCount);
                    UnsafeUtility.MemSet(Visited.GetUnsafePtr(), 0, GridCellCount);
                    UnsafeUtility.MemSet(CostMap.GetUnsafePtr(), 255, GridCellCount * 2); // ushort.MaxValue = 0xFFFF
                }

                FlowFieldCore.ComputeField(
                    PassabilityMap, Destinations[i], GridSize,
                    outputField, BfsQueue, Visited, CostMap);
            }
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateAfter(typeof(GridObstacleResponseSystem))]
    public partial struct FlowFieldSystem : ISystem
    {
        const int MaxFields = 32;
        const int WorkerCount = 8;

        // 워커 메모리 (Persistent, flat)
        NativeArray<int2> _workerBfsQueues;    // WorkerCount * gridCellCount
        NativeArray<byte> _workerVisited;      // WorkerCount * gridCellCount
        NativeArray<ushort> _workerCostMaps;   // WorkerCount * gridCellCount

        bool _initialized;
        int _gridCellCount;
        int2 _gridSize;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GridSettings>();
            state.RequireForUpdate<MovementGoal>();
            _initialized = false;
        }

        public void OnDestroy(ref SystemState state)
        {
            if (!_initialized) return;

            // 워커 메모리 Dispose
            if (_workerBfsQueues.IsCreated) _workerBfsQueues.Dispose();
            if (_workerVisited.IsCreated) _workerVisited.Dispose();
            if (_workerCostMaps.IsCreated) _workerCostMaps.Dispose();

            // FlowFieldCacheData Dispose
            if (!SystemAPI.TryGetSingleton<FlowFieldCacheData>(out var cache)) return;

            if (cache.SmallFieldPool.IsCreated) cache.SmallFieldPool.Dispose();
            if (cache.LargeFieldPool.IsCreated) cache.LargeFieldPool.Dispose();
            if (cache.SmallKeyToPoolIndex.IsCreated) cache.SmallKeyToPoolIndex.Dispose();
            if (cache.LargeKeyToPoolIndex.IsCreated) cache.LargeKeyToPoolIndex.Dispose();
            if (cache.SmallFieldLastUsedFrame.IsCreated) cache.SmallFieldLastUsedFrame.Dispose();
            if (cache.LargeFieldLastUsedFrame.IsCreated) cache.LargeFieldLastUsedFrame.Dispose();
            if (cache.SmallPassabilityMap.IsCreated) cache.SmallPassabilityMap.Dispose();
            if (cache.LargePassabilityMap.IsCreated) cache.LargePassabilityMap.Dispose();
        }

        public void OnUpdate(ref SystemState state)
        {
            // 선행 시스템(UnifiedTargetingSystem 등)의 Job 완료 대기
            state.CompleteDependency();

            var gridSettings = SystemAPI.GetSingleton<GridSettings>();

            if (!_initialized)
            {
                Initialize(ref state, gridSettings);
                _initialized = true;
            }

            var cache = SystemAPI.GetSingleton<FlowFieldCacheData>();
            var gridEntity = SystemAPI.GetSingletonEntity<GridSettings>();
            var gridBuffer = SystemAPI.GetBuffer<GridCell>(gridEntity);
            uint currentFrame = (uint)state.GlobalSystemVersion;

            // === Phase 0: Passability 맵 갱신 (조건부) ===
            if (cache.IsGridStale)
            {
                // 캐시 전체 무효화
                cache.SmallKeyToPoolIndex.Clear();
                cache.LargeKeyToPoolIndex.Clear();
                unsafe
                {
                    UnsafeUtility.MemSet(cache.SmallFieldLastUsedFrame.GetUnsafePtr(), 0, MaxFields * 4);
                    UnsafeUtility.MemSet(cache.LargeFieldLastUsedFrame.GetUnsafePtr(), 0, MaxFields * 4);
                }

                GridUtility.BuildPassabilityMap(gridBuffer, _gridSize, 0, cache.SmallPassabilityMap);
                GridUtility.BuildPassabilityMap(gridBuffer, _gridSize, 1, cache.LargePassabilityMap);
                cache.IsGridStale = false;
            }

            // === Phase 1: Collect ===
            var smallUnits = new NativeList<PendingFlowFieldUnit>(64, Allocator.Temp);
            var largeUnits = new NativeList<PendingFlowFieldUnit>(16, Allocator.Temp);
            var smallMissKeys = new NativeList<int>(16, Allocator.Temp);
            var smallMissDests = new NativeList<int2>(16, Allocator.Temp);
            var largeMissKeys = new NativeList<int>(16, Allocator.Temp);
            var largeMissDests = new NativeList<int2>(16, Allocator.Temp);
            var smallMissKeySet = new NativeHashSet<int>(16, Allocator.Temp);
            var largeMissKeySet = new NativeHashSet<int>(16, Allocator.Temp);

            int gridSizeX = _gridSize.x;
            int gridSizeY = _gridSize.y;

            // 지상 유닛 수집 (FlyingTag 제외)
            foreach (var (goal, transform, pathSize, entity) in
                SystemAPI.Query<RefRO<MovementGoal>, RefRO<LocalTransform>, RefRO<GridPathfindingSize>>()
                    .WithNone<FlyingTag>()
                    .WithAny<UnitTag, EnemyTag>()
                    .WithEntityAccess())
            {
                if (!goal.ValueRO.IsPathDirty)
                    continue;

                int2 destCell = GridUtility.WorldToGrid(goal.ValueRO.Destination, gridSettings);

                // 범위 검증 — 범위 밖이면 Partial Path 처리 (Apply에서 실패로 처리)
                if (destCell.x < 0 || destCell.x >= gridSizeX || destCell.y < 0 || destCell.y >= gridSizeY)
                {
                    destCell = math.clamp(destCell, int2.zero, new int2(gridSizeX - 1, gridSizeY - 1));
                }

                int destKey = destCell.y * gridSizeX + destCell.x;
                int2 currentCell = GridUtility.WorldToGrid(transform.ValueRO.Position, gridSettings);

                var pending = new PendingFlowFieldUnit
                {
                    Entity = entity,
                    DestinationKey = destKey,
                    CurrentCell = currentCell,
                };

                byte cellPadding = pathSize.ValueRO.CellPadding;
                if (cellPadding == 0)
                {
                    smallUnits.Add(pending);
                    if (!cache.SmallKeyToPoolIndex.ContainsKey(destKey) && smallMissKeySet.Add(destKey))
                    {
                        smallMissKeys.Add(destKey);
                        smallMissDests.Add(destCell);
                    }
                }
                else
                {
                    largeUnits.Add(pending);
                    if (!cache.LargeKeyToPoolIndex.ContainsKey(destKey) && largeMissKeySet.Add(destKey))
                    {
                        largeMissKeys.Add(destKey);
                        largeMissDests.Add(destCell);
                    }
                }
            }

            // Flying 유닛 별도 처리: 직선 이동
            foreach (var (goal, waypoints, entity) in
                SystemAPI.Query<RefRW<MovementGoal>, RefRW<MovementWaypoints>>()
                    .WithAll<FlyingTag>()
                    .WithAny<UnitTag, EnemyTag>()
                    .WithEntityAccess())
            {
                if (!goal.ValueRO.IsPathDirty)
                    continue;

                goal.ValueRW.IsPathDirty = false;
                waypoints.ValueRW.Current = goal.ValueRO.Destination;
                waypoints.ValueRW.HasNext = false;
                state.EntityManager.SetComponentEnabled<MovementWaypoints>(entity, true);
            }

            if (smallUnits.Length == 0 && largeUnits.Length == 0)
            {
                SystemAPI.SetSingleton(cache);
                return;
            }

            // === Phase 2: Compute (캐시 미스 BFS 계산) ===
            int totalSmallMisses = smallMissKeys.Length;
            int totalLargeMisses = largeMissKeys.Length;

            // Small 캐시 미스에 풀 인덱스 할당 (TempJob: Job에서 참조)
            var smallMissPoolIndices = new NativeArray<int>(totalSmallMisses, Allocator.TempJob);
            var smallMissDestsArray = new NativeArray<int2>(totalSmallMisses, Allocator.TempJob);
            for (int i = 0; i < totalSmallMisses; i++)
            {
                int poolIndex = AllocatePoolSlot(cache.SmallKeyToPoolIndex, cache.SmallFieldLastUsedFrame, currentFrame);
                cache.SmallKeyToPoolIndex.Add(smallMissKeys[i], poolIndex);
                cache.SmallFieldLastUsedFrame[poolIndex] = currentFrame;
                smallMissPoolIndices[i] = poolIndex;
                smallMissDestsArray[i] = smallMissDests[i];
            }

            // Large 캐시 미스에 풀 인덱스 할당
            var largeMissPoolIndices = new NativeArray<int>(totalLargeMisses, Allocator.TempJob);
            var largeMissDestsArray = new NativeArray<int2>(totalLargeMisses, Allocator.TempJob);
            for (int i = 0; i < totalLargeMisses; i++)
            {
                int poolIndex = AllocatePoolSlot(cache.LargeKeyToPoolIndex, cache.LargeFieldLastUsedFrame, currentFrame);
                cache.LargeKeyToPoolIndex.Add(largeMissKeys[i], poolIndex);
                cache.LargeFieldLastUsedFrame[poolIndex] = currentFrame;
                largeMissPoolIndices[i] = poolIndex;
                largeMissDestsArray[i] = largeMissDests[i];
            }

            // Small BFS Job 스케줄링 → 완료 → Large BFS Job 스케줄링 → 완료
            // (워커 버퍼를 Small/Large가 공유하므로 순차 실행 필수)
            var smallJobHandles = ScheduleComputeJobs(
                smallMissDestsArray, smallMissPoolIndices,
                cache.SmallPassabilityMap, cache.SmallFieldPool,
                totalSmallMisses);
            smallJobHandles.Complete();

            var largeJobHandles = ScheduleComputeJobs(
                largeMissDestsArray, largeMissPoolIndices,
                cache.LargePassabilityMap, cache.LargeFieldPool,
                totalLargeMisses);
            largeJobHandles.Complete();

            // TempJob 배열 해제
            smallMissPoolIndices.Dispose();
            smallMissDestsArray.Dispose();
            largeMissPoolIndices.Dispose();
            largeMissDestsArray.Dispose();

            // 캐시 히트 LRU 갱신
            for (int i = 0; i < smallUnits.Length; i++)
            {
                if (cache.SmallKeyToPoolIndex.TryGetValue(smallUnits[i].DestinationKey, out int poolIdx))
                    cache.SmallFieldLastUsedFrame[poolIdx] = currentFrame;
            }
            for (int i = 0; i < largeUnits.Length; i++)
            {
                if (cache.LargeKeyToPoolIndex.TryGetValue(largeUnits[i].DestinationKey, out int poolIdx))
                    cache.LargeFieldLastUsedFrame[poolIdx] = currentFrame;
            }

            // === Phase 3: Apply ===
            var goalLookup = SystemAPI.GetComponentLookup<MovementGoal>();
            var waypointsLookup = SystemAPI.GetComponentLookup<MovementWaypoints>();
            var flowFieldRefLookup = SystemAPI.GetComponentLookup<FlowFieldRef>();

            // Small 유닛 Apply
            ApplyFlowFieldResults(ref state, smallUnits, cache.SmallKeyToPoolIndex,
                cache.SmallFieldPool, goalLookup, waypointsLookup, flowFieldRefLookup, gridSettings);

            // Apply 후 Lookup 갱신 (Small Apply가 데이터 변경했을 수 있음)
            goalLookup.Update(ref state);
            waypointsLookup.Update(ref state);
            flowFieldRefLookup.Update(ref state);

            // Large 유닛 Apply
            ApplyFlowFieldResults(ref state, largeUnits, cache.LargeKeyToPoolIndex,
                cache.LargeFieldPool, goalLookup, waypointsLookup, flowFieldRefLookup, gridSettings);

            SystemAPI.SetSingleton(cache);
        }

        void Initialize(ref SystemState state, GridSettings gridSettings)
        {
            _gridSize = gridSettings.GridSize;
            _gridCellCount = _gridSize.x * _gridSize.y;

            // 워커 메모리 할당
            _workerBfsQueues = new NativeArray<int2>(WorkerCount * _gridCellCount, Allocator.Persistent);
            _workerVisited = new NativeArray<byte>(WorkerCount * _gridCellCount, Allocator.Persistent);
            _workerCostMaps = new NativeArray<ushort>(WorkerCount * _gridCellCount, Allocator.Persistent);

            // FlowFieldCacheData 싱글톤 생성
            var cache = new FlowFieldCacheData
            {
                SmallFieldPool = new NativeArray<byte>(MaxFields * _gridCellCount, Allocator.Persistent),
                LargeFieldPool = new NativeArray<byte>(MaxFields * _gridCellCount, Allocator.Persistent),
                SmallKeyToPoolIndex = new NativeHashMap<int, int>(MaxFields, Allocator.Persistent),
                LargeKeyToPoolIndex = new NativeHashMap<int, int>(MaxFields, Allocator.Persistent),
                GridCellCount = _gridCellCount,
                IsGridStale = true, // 첫 프레임에 passability 맵 빌드 트리거
                SmallFieldLastUsedFrame = new NativeArray<uint>(MaxFields, Allocator.Persistent),
                LargeFieldLastUsedFrame = new NativeArray<uint>(MaxFields, Allocator.Persistent),
                SmallPassabilityMap = new NativeArray<byte>(_gridCellCount, Allocator.Persistent),
                LargePassabilityMap = new NativeArray<byte>(_gridCellCount, Allocator.Persistent),
            };

            var entity = state.EntityManager.CreateEntity(typeof(FlowFieldCacheData));
            state.EntityManager.SetComponentData(entity, cache);
        }

        /// <summary>
        /// LRU 기반 풀 슬롯 할당: 빈 슬롯 우선, 없으면 가장 오래된 슬롯 교체
        /// </summary>
        static int AllocatePoolSlot(
            NativeHashMap<int, int> keyToPoolIndex,
            NativeArray<uint> lastUsedFrame,
            uint currentFrame)
        {
            // 빈 슬롯 찾기 (lastUsedFrame == 0)
            for (int i = 0; i < lastUsedFrame.Length; i++)
            {
                if (lastUsedFrame[i] == 0)
                    return i;
            }

            // LRU: 가장 오래된 슬롯 찾기
            int oldestIndex = 0;
            uint oldestFrame = uint.MaxValue;
            for (int i = 0; i < lastUsedFrame.Length; i++)
            {
                if (lastUsedFrame[i] < oldestFrame)
                {
                    oldestFrame = lastUsedFrame[i];
                    oldestIndex = i;
                }
            }

            // 교체할 슬롯의 기존 키 제거
            var keysToRemove = new NativeList<int>(4, Allocator.Temp);
            foreach (var kvp in keyToPoolIndex)
            {
                if (kvp.Value == oldestIndex)
                    keysToRemove.Add(kvp.Key);
            }
            for (int i = 0; i < keysToRemove.Length; i++)
                keyToPoolIndex.Remove(keysToRemove[i]);

            return oldestIndex;
        }

        JobHandle ScheduleComputeJobs(
            NativeArray<int2> destinations,
            NativeArray<int> poolIndices,
            NativeArray<byte> passabilityMap,
            NativeArray<byte> fieldPool,
            int missCount)
        {
            if (missCount == 0)
                return default;

            int workerCount = math.min(missCount, WorkerCount);
            int batchSize = (missCount + workerCount - 1) / workerCount;
            var handles = new NativeArray<JobHandle>(workerCount, Allocator.Temp);

            for (int w = 0; w < workerCount; w++)
            {
                int start = w * batchSize;
                if (start >= missCount) break;
                int end = math.min(start + batchSize, missCount);

                handles[w] = new FlowFieldComputeJob
                {
                    StartIndex = start,
                    EndIndex = end,
                    Destinations = destinations,
                    PoolIndices = poolIndices,
                    PassabilityMap = passabilityMap,
                    GridSize = _gridSize,
                    GridCellCount = _gridCellCount,
                    BfsQueue = _workerBfsQueues.GetSubArray(w * _gridCellCount, _gridCellCount),
                    Visited = _workerVisited.GetSubArray(w * _gridCellCount, _gridCellCount),
                    CostMap = _workerCostMaps.GetSubArray(w * _gridCellCount, _gridCellCount),
                    FieldPool = fieldPool,
                }.Schedule();
            }

            var combined = JobHandle.CombineDependencies(handles.GetSubArray(0, workerCount));
            handles.Dispose();
            return combined;
        }

        static void ApplyFlowFieldResults(
            ref SystemState state,
            NativeList<PendingFlowFieldUnit> units,
            NativeHashMap<int, int> keyToPoolIndex,
            NativeArray<byte> fieldPool,
            ComponentLookup<MovementGoal> goalLookup,
            ComponentLookup<MovementWaypoints> waypointsLookup,
            ComponentLookup<FlowFieldRef> flowFieldRefLookup,
            GridSettings gridSettings)
        {
            int gridSizeX = gridSettings.GridSize.x;
            int gridSizeY = gridSettings.GridSize.y;
            int gridCellCount = gridSizeX * gridSizeY;

            for (int i = 0; i < units.Length; i++)
            {
                var pending = units[i];
                Entity entity = pending.Entity;

                if (!goalLookup.HasComponent(entity))
                    continue;

                var goal = goalLookup[entity];
                goal.IsPathDirty = false;

                // FlowFieldRef 할당
                if (flowFieldRefLookup.HasComponent(entity))
                {
                    flowFieldRefLookup[entity] = new FlowFieldRef { Key = pending.DestinationKey };
                }

                if (!keyToPoolIndex.TryGetValue(pending.DestinationKey, out int poolIndex))
                {
                    // 캐시에 없음 (이론상 발생 불가)
                    goal.IsPathPartial = true;
                    goalLookup[entity] = goal;
                    waypointsLookup.SetComponentEnabled(entity, false);
                    continue;
                }

                var field = fieldPool.GetSubArray(poolIndex * gridCellCount, gridCellCount);

                // 유닛 현재 셀의 방향 확인
                int2 currentCell = pending.CurrentCell;
                currentCell = math.clamp(currentCell, int2.zero, new int2(gridSizeX - 1, gridSizeY - 1));
                int currentIndex = currentCell.y * gridSizeX + currentCell.x;
                byte currentDir = field[currentIndex];

                if (currentDir != FlowFieldCore.DirNone)
                {
                    // 도달 가능 — 정상 경로
                    var waypoints = waypointsLookup[entity];
                    int2 nextCell = currentCell + FlowFieldCore.GetDirectionOffset(currentDir);
                    waypoints.Current = GridUtility.CellCenterToWorld(nextCell, gridSettings);
                    waypoints.HasNext = false;
                    goal.IsPathPartial = false;
                    goalLookup[entity] = goal;
                    waypointsLookup[entity] = waypoints;
                    waypointsLookup.SetComponentEnabled(entity, true);
                }
                else
                {
                    // 현재 셀 도달 불가 — 8방향 1단계 탐색
                    bool foundReachable = false;
                    for (byte dir = 0; dir < 8; dir++)
                    {
                        int2 offset = FlowFieldCore.GetDirectionOffset(dir);
                        int2 neighbor = currentCell + offset;

                        if (neighbor.x < 0 || neighbor.y < 0 || neighbor.x >= gridSizeX || neighbor.y >= gridSizeY)
                            continue;

                        int neighborIndex = neighbor.y * gridSizeX + neighbor.x;
                        if (field[neighborIndex] != FlowFieldCore.DirNone)
                        {
                            var waypoints = waypointsLookup[entity];
                            waypoints.Current = GridUtility.CellCenterToWorld(neighbor, gridSettings);
                            waypoints.HasNext = false;
                            goal.IsPathPartial = true;
                            goalLookup[entity] = goal;
                            waypointsLookup[entity] = waypoints;
                            waypointsLookup.SetComponentEnabled(entity, true);
                            foundReachable = true;
                            break;
                        }
                    }

                    if (!foundReachable)
                    {
                        // 8방향 모두 도달 불가 — 유닛 정지
                        goal.IsPathPartial = true;
                        goalLookup[entity] = goal;
                        waypointsLookup.SetComponentEnabled(entity, false);
                    }
                }
            }
        }
    }
}
