using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using Shared;

namespace Server
{
    /// <summary>
    /// 통합 타겟팅 시스템
    /// <para>- EnemyTargetJob: 적 → 아군 타겟팅 (배회 로직 + 사망 체크 포함)</para>
    /// <para>- UnitAutoTargetJob: 유닛 → 적 자동 감지</para>
    /// <para>- 2개 Job 순차 실행 (AggroTarget 공유로 인한 Job Safety)</para>
    /// <para>- 타겟 고착화 (Hysteresis) 적용</para>
    /// <para>- 시간 분할 (Time Slicing) 적용</para>
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(SpatialPartitioningGroup))]
    [UpdateAfter(typeof(HandleAttackRequestSystem))]
    [UpdateBefore(typeof(PathfindingSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [BurstCompile]
    public partial struct UnifiedTargetingSystem : ISystem
    {
        private ComponentLookup<LocalTransform> _transformLookup;
        private ComponentLookup<Health> _healthLookup;
        private uint _frameCount;

        public void OnCreate(ref SystemState state)
        {
            _transformLookup = state.GetComponentLookup<LocalTransform>(isReadOnly: true);
            _healthLookup = state.GetComponentLookup<Health>(isReadOnly: true);

            state.RequireForUpdate<GridSettings>();
            state.RequireForUpdate<SpatialMaps>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            _transformLookup.Update(ref state);
            _healthLookup.Update(ref state);
            _frameCount++;

            // SpatialMaps 싱글톤에서 맵 가져오기
            if (!SystemAPI.TryGetSingleton<SpatialMaps>(out var spatialMaps) || !spatialMaps.IsValid)
            {
                // 맵이 없으면 배회 전용 Job만 실행
                var gridSettings = SystemAPI.GetSingleton<GridSettings>();
                float elapsedTime = (float)SystemAPI.Time.ElapsedTime;

                var wanderOnlyJob = new EnemyWanderOnlyJob
                {
                    GridSettings = gridSettings,
                    ElapsedTime = elapsedTime,
                    FrameCount = _frameCount
                };
                state.Dependency = wanderOnlyJob.ScheduleParallel(state.Dependency);
                return;
            }

            var gridSettingsData = SystemAPI.GetSingleton<GridSettings>();
            float time = (float)SystemAPI.Time.ElapsedTime;

            // 적 타겟팅 Job (적 → 아군 타겟팅)
            var enemyTargetJob = new EnemyTargetJob
            {
                TargetingMap = spatialMaps.TargetingMap,
                TransformLookup = _transformLookup,
                HealthLookup = _healthLookup,
                GridSettings = gridSettingsData,
                ElapsedTime = time,
                CellSize = SpatialHashUtility.TargetingCellSize,
                FrameCount = _frameCount
            };

            // 유닛 자동 타겟팅 Job (유닛 → 적 자동 감지)
            var unitAutoTargetJob = new UnitAutoTargetJob
            {
                TargetingMap = spatialMaps.TargetingMap,
                TransformLookup = _transformLookup,
                HealthLookup = _healthLookup,
                CellSize = SpatialHashUtility.TargetingCellSize,
                FrameCount = _frameCount
            };

            // 순차 실행 (AggroTarget 컴포넌트를 공유하므로 병렬 불가)
            // 내부적으로는 IJobEntity가 병렬 처리하므로 성능 영향 최소
            var handle1 = enemyTargetJob.ScheduleParallel(state.Dependency);
            state.Dependency = unitAutoTargetJob.ScheduleParallel(handle1);
        }
    }

    // =========================================================================
    // 적 타겟팅 Job (적 → 아군)
    // =========================================================================

    [BurstCompile]
    [WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)]
    public partial struct EnemyTargetJob : IJobEntity
    {
        [ReadOnly] public NativeParallelMultiHashMap<int, SpatialTargetEntry> TargetingMap;
        [ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;
        [ReadOnly] public ComponentLookup<Health> HealthLookup;
        [ReadOnly] public GridSettings GridSettings;
        public float ElapsedTime;
        public float CellSize;
        public uint FrameCount;

        // 목적지 변경 역치 (1m 이상 차이나야 경로 재계산)
        private const float DestinationThresholdSq = 1.0f;
        // 타겟 고착화 계수 (LoseTargetDistance의 1.3배)
        private const float HysteresisMultiplier = 1.3f;
        // 시간 분할 주기 (4프레임에 1번 탐색)
        private const uint TimeSliceDivisor = 4;
        // Partial Path 재시도 최소 간격 (초)
        private const float PathRetryInterval = 2.0f;
        // 위치 정체 체크 간격 (초)
        private const float StuckCheckInterval = 3.0f;
        // stuck 판정 이동 거리 (미터)
        private const float StuckThreshold = 2.0f;

        public void Execute(
            Entity entity,
            RefRO<LocalTransform> myTransform,
            RefRW<AggroTarget> target,
            RefRO<AggroLock> aggroLock,
            RefRW<EnemyState> enemyState,
            RefRO<EnemyChaseDistance> chaseDistance,
            RefRO<Team> myTeam,
            RefRW<MovementGoal> goal,
            EnabledRefRW<MovementWaypoints> waypointsEnabled,
            in EnemyTag enemyTag)
        {
            float3 myPos = myTransform.ValueRO.Position;
            bool needNewTarget = false;
            float loseDistSq = chaseDistance.ValueRO.LoseTargetDistance * chaseDistance.ValueRO.LoseTargetDistance;
            // 타겟 고착화: 더 멀어져야 타겟 놓침
            float hysteresisDistSq = loseDistSq * (HysteresisMultiplier * HysteresisMultiplier);

            // ---------------------------------------------------------
            // 0. 어그로 고정 체크 - 고정 중이면 타겟 변경 불가
            // ---------------------------------------------------------
            if (aggroLock.ValueRO.RemainingLockTime > 0f && aggroLock.ValueRO.LockedTarget != Entity.Null)
            {
                Entity lockedTarget = aggroLock.ValueRO.LockedTarget;

                // 고정된 타겟 유효성 체크
                if (TransformLookup.TryGetComponent(lockedTarget, out LocalTransform lockedTransform) &&
                    HealthLookup.TryGetComponent(lockedTarget, out Health lockedHealth) &&
                    lockedHealth.CurrentValue > 0)
                {
                    float3 lockedPos = lockedTransform.Position;
                    float distSqToLocked = math.distancesq(myPos, lockedPos);

                    // 어그로 범위 내면 고정 타겟 유지
                    if (distSqToLocked <= hysteresisDistSq)
                    {
                        target.ValueRW.TargetEntity = lockedTarget;
                        target.ValueRW.LastTargetPosition = lockedPos;

                        float3 currentDest = goal.ValueRO.Destination;
                        if (math.distancesq(currentDest, lockedPos) > DestinationThresholdSq)
                        {
                            goal.ValueRW.Destination = lockedPos;
                            goal.ValueRW.IsPathDirty = true;
                            goal.ValueRW.IsPathPartial = false;
                            goal.ValueRW.DestinationSetTime = ElapsedTime;
                        }
                        else if (goal.ValueRO.IsPathPartial)
                        {
                            // Partial path: 시간 게이트 + 프레임 분산으로 재시도
                            if (ElapsedTime - goal.ValueRO.DestinationSetTime >= PathRetryInterval)
                            {
                                if (FrameCount % TimeSliceDivisor == (uint)entity.Index % TimeSliceDivisor)
                                {
                                    goal.ValueRW.IsPathDirty = true;
                                    goal.ValueRW.DestinationSetTime = ElapsedTime;
                                }
                            }
                        }

                        enemyState.ValueRW.CurrentState = EnemyContext.Chasing;
                        return;  // 고정 타겟 유지, 탐색 스킵
                    }
                }
                // 고정 타겟이 무효하거나 범위 벗어남 → 정상 타겟팅으로 진행
            }

            // ---------------------------------------------------------
            // 1. 현재 타겟 유효성 검사 (고착화 적용 + 사망 체크)
            // ---------------------------------------------------------
            Entity currentTarget = target.ValueRO.TargetEntity;
            if (currentTarget == Entity.Null)
            {
                needNewTarget = true;
            }
            else
            {
                // Transform + Health 유효성 검사 (시체 공격 방지)
                if (!TransformLookup.TryGetComponent(currentTarget, out LocalTransform targetTransform) ||
                    !HealthLookup.TryGetComponent(currentTarget, out Health targetHealth) ||
                    targetHealth.CurrentValue <= 0)
                {
                    needNewTarget = true;
                }
                else
                {
                    float3 targetPos = targetTransform.Position;
                    float distSq = math.distancesq(myPos, targetPos);

                    // 고착화: 원래 범위의 1.3배까지 타겟 유지
                    if (distSq > hysteresisDistSq)
                    {
                        needNewTarget = true;
                    }
                    else
                    {
                        target.ValueRW.LastTargetPosition = targetPos;

                        float3 currentDest = goal.ValueRO.Destination;
                        if (math.distancesq(currentDest, targetPos) > DestinationThresholdSq)
                        {
                            goal.ValueRW.Destination = targetPos;
                            goal.ValueRW.IsPathDirty = true;
                            goal.ValueRW.IsPathPartial = false;
                            goal.ValueRW.DestinationSetTime = ElapsedTime;
                        }
                        else if (goal.ValueRO.IsPathPartial)
                        {
                            // Partial path: 시간 게이트 + 프레임 분산으로 재시도
                            if (ElapsedTime - goal.ValueRO.DestinationSetTime >= PathRetryInterval)
                            {
                                if (FrameCount % TimeSliceDivisor == (uint)entity.Index % TimeSliceDivisor)
                                {
                                    goal.ValueRW.IsPathDirty = true;
                                    goal.ValueRW.DestinationSetTime = ElapsedTime;
                                }
                            }
                        }
                    }
                }
            }

            if (!needNewTarget) return;

            // ---------------------------------------------------------
            // 시간 분할: 배회 중(타겟 없음)이면 4프레임에 1번만 탐색
            // 타겟 상실(hadTarget=true)이면 전투 반응성을 위해 즉시 탐색
            // ---------------------------------------------------------
            bool hadTarget = currentTarget != Entity.Null;

            if (!hadTarget)
            {
                uint frameSlice = FrameCount % TimeSliceDivisor;
                bool isMySearchFrame = ((uint)entity.Index % TimeSliceDivisor) == frameSlice;
                if (!isMySearchFrame)
                {
                    target.ValueRW.TargetEntity = Entity.Null;

                    // stuck 감지: 배회 중 위치 정체 시 재배회
                    bool forceRefresh = false;
                    if (enemyState.ValueRO.CurrentState == EnemyContext.Wandering)
                    {
                        if (ElapsedTime - goal.ValueRO.LastPositionCheckTime >= StuckCheckInterval)
                        {
                            float movedDistance = math.distance(myPos, goal.ValueRO.LastPositionCheck);
                            if (movedDistance < StuckThreshold)
                            {
                                forceRefresh = true;
                            }
                            goal.ValueRW.LastPositionCheckTime = ElapsedTime;
                            goal.ValueRW.LastPositionCheck = myPos;
                        }
                    }

                    bool needNewWanderTarget = enemyState.ValueRO.CurrentState != EnemyContext.Wandering
                                               || !waypointsEnabled.ValueRO
                                               || forceRefresh;
                    if (needNewWanderTarget)
                    {
                        uint seed = (uint)entity.Index ^ (FrameCount * 0x9E3779B9) ^ (uint)(ElapsedTime * 1000);
                        var random = Random.CreateFromIndex(seed);
                        float2 mapMin = GridSettings.GridOrigin;
                        float2 mapMax = mapMin + new float2(
                            GridSettings.GridSize.x * GridSettings.CellSize,
                            GridSettings.GridSize.y * GridSettings.CellSize);
                        float3 wanderDest = new float3(
                            random.NextFloat(mapMin.x + 5f, mapMax.x - 5f),
                            myPos.y,
                            random.NextFloat(mapMin.y + 5f, mapMax.y - 5f));
                        goal.ValueRW.Destination = wanderDest;
                        goal.ValueRW.IsPathDirty = true;
                        goal.ValueRW.DestinationSetTime = ElapsedTime;
                        goal.ValueRW.LastPositionCheckTime = ElapsedTime;
                        goal.ValueRW.LastPositionCheck = myPos;
                        waypointsEnabled.ValueRW = true;
                    }
                    enemyState.ValueRW.CurrentState = EnemyContext.Wandering;
                    return;
                }
            }

            // ---------------------------------------------------------
            // 2. 새로운 타겟 탐색 (공간 분할 기반)
            // ---------------------------------------------------------
            Entity bestTarget = Entity.Null;
            float bestDistSq = float.MaxValue;
            float3 bestTargetPos = float3.zero;
            float searchRadiusSq = loseDistSq;

            // 탐색 범위: aggroRange / CellSize (올림)
            int searchRadius = (int)math.ceil(chaseDistance.ValueRO.LoseTargetDistance / CellSize);
            for (int x = -searchRadius; x <= searchRadius; x++)
            {
                for (int z = -searchRadius; z <= searchRadius; z++)
                {
                    int hash = SpatialHashUtility.GetCellHash(myPos, x, z, CellSize);

                    if (TargetingMap.TryGetFirstValue(hash, out SpatialTargetEntry candidate, out var it))
                    {
                        do
                        {
                            // 자기 자신 제외
                            if (candidate.Entity == entity) continue;

                            // 같은 팀이면 공격 안 함 (적은 teamId가 다름)
                            if (candidate.TeamId == myTeam.ValueRO.teamId) continue;

                            float distSq = math.distancesq(myPos, candidate.Position);

                            if (distSq < searchRadiusSq && distSq < bestDistSq)
                            {
                                bestDistSq = distSq;
                                bestTarget = candidate.Entity;
                                bestTargetPos = candidate.Position;
                            }

                        } while (TargetingMap.TryGetNextValue(out candidate, ref it));
                    }
                }
            }

            // ---------------------------------------------------------
            // 3. 결과 적용
            // ---------------------------------------------------------
            Entity previousTarget = target.ValueRO.TargetEntity;

            if (bestTarget != Entity.Null)
            {
                target.ValueRW.TargetEntity = bestTarget;
                target.ValueRW.LastTargetPosition = bestTargetPos;

                bool targetChanged = previousTarget != bestTarget;
                float3 currentDest = goal.ValueRO.Destination;

                if (targetChanged || math.distancesq(currentDest, bestTargetPos) > DestinationThresholdSq)
                {
                    goal.ValueRW.Destination = bestTargetPos;
                    goal.ValueRW.IsPathDirty = true;
                    goal.ValueRW.IsPathPartial = false;
                    goal.ValueRW.DestinationSetTime = ElapsedTime;
                }

                enemyState.ValueRW.CurrentState = EnemyContext.Chasing;
            }
            else
            {
                target.ValueRW.TargetEntity = Entity.Null;

                // ---------------------------------------------------------
                // 배회 로직
                // ---------------------------------------------------------
                // stuck 감지: 배회 중 위치 정체 시 재배회
                bool forceRefresh = false;
                if (enemyState.ValueRO.CurrentState == EnemyContext.Wandering)
                {
                    if (ElapsedTime - goal.ValueRO.LastPositionCheckTime >= StuckCheckInterval)
                    {
                        float movedDistance = math.distance(myPos, goal.ValueRO.LastPositionCheck);
                        if (movedDistance < StuckThreshold)
                        {
                            forceRefresh = true;
                        }
                        goal.ValueRW.LastPositionCheckTime = ElapsedTime;
                        goal.ValueRW.LastPositionCheck = myPos;
                    }
                }

                bool needNewWanderTarget = enemyState.ValueRO.CurrentState != EnemyContext.Wandering
                                           || !waypointsEnabled.ValueRO
                                           || forceRefresh;

                if (needNewWanderTarget)
                {
                    // 황금비 매직 넘버로 비트 패턴을 잘 섞어줌
                    uint seed = (uint)entity.Index ^ (FrameCount * 0x9E3779B9) ^ (uint)(ElapsedTime * 1000);
                    var random = Random.CreateFromIndex(seed);

                    float2 mapMin = GridSettings.GridOrigin;
                    float2 mapMax = mapMin + new float2(
                        GridSettings.GridSize.x * GridSettings.CellSize,
                        GridSettings.GridSize.y * GridSettings.CellSize
                    );

                    float3 wanderDest = new float3(
                        random.NextFloat(mapMin.x + 5f, mapMax.x - 5f),
                        myPos.y,
                        random.NextFloat(mapMin.y + 5f, mapMax.y - 5f)
                    );

                    goal.ValueRW.Destination = wanderDest;
                    goal.ValueRW.IsPathDirty = true;
                    goal.ValueRW.DestinationSetTime = ElapsedTime;
                    goal.ValueRW.LastPositionCheckTime = ElapsedTime;
                    goal.ValueRW.LastPositionCheck = myPos;
                    waypointsEnabled.ValueRW = true;
                }

                enemyState.ValueRW.CurrentState = EnemyContext.Wandering;
            }
        }
    }

    // =========================================================================
    // 유닛 자동 타겟팅 Job (유닛 → 적)
    // =========================================================================

    [BurstCompile]
    [WithAll(typeof(UnitTag), typeof(CombatStats))]
    [WithNone(typeof(WorkerTag))]
    [WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)]
    public partial struct UnitAutoTargetJob : IJobEntity
    {
        [ReadOnly] public NativeParallelMultiHashMap<int, SpatialTargetEntry> TargetingMap;
        [ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;
        [ReadOnly] public ComponentLookup<Health> HealthLookup;
        public float CellSize;
        public uint FrameCount;

        private const float DestinationThresholdSq = 1.0f;
        // 타겟 고착화 계수 (VisionRange의 1.3배)
        private const float HysteresisMultiplier = 1.3f;
        // 시간 분할 주기 (4프레임에 1번 탐색)
        private const uint TimeSliceDivisor = 4;

        public void Execute(
            Entity entity,
            RefRO<LocalTransform> myTransform,
            RefRO<VisionRange> visionRange,
            RefRW<UnitIntentState> intent,
            RefRW<AggroTarget> aggroTarget,
            RefRW<AggroLock> aggroLock,
            RefRW<MovementGoal> goal,
            EnabledRefRW<MovementWaypoints> waypointsEnabled)
        {
            // ---------------------------------------------------------
            // 0. 어그로 고정 중이면 자동 타겟팅 스킵
            // ---------------------------------------------------------
            if (aggroLock.ValueRO.RemainingLockTime > 0f && aggroLock.ValueRO.LockedTarget != Entity.Null)
            {
                // 어그로 고정 상태 - 자동 타겟팅하지 않음
                // AggroReactionSystem에서 이미 타겟 설정됨
                return;
            }

            // ---------------------------------------------------------
            // 1. 자동 타겟팅 활성화 조건 체크
            // ---------------------------------------------------------
            Intent currentIntent = intent.ValueRO.State;
            if (currentIntent != Intent.Idle && currentIntent != Intent.AttackMove && currentIntent != Intent.Attack)
                return;

            float3 myPos = myTransform.ValueRO.Position;
            float visionRangeSq = visionRange.ValueRO.Value * visionRange.ValueRO.Value;
            // 타겟 고착화 범위
            float hysteresisRangeSq = visionRangeSq * (HysteresisMultiplier * HysteresisMultiplier);

            // ---------------------------------------------------------
            // 2. 현재 타겟 유효성 검사 (Early Exit 제거 → 시간 분할로 이어짐)
            // ---------------------------------------------------------
            Entity currentTarget = aggroTarget.ValueRO.TargetEntity;
            bool needImmediateSearch = currentTarget == Entity.Null;

            if (currentTarget != Entity.Null)
            {
                if (TransformLookup.TryGetComponent(currentTarget, out LocalTransform targetTransform) &&
                    HealthLookup.TryGetComponent(currentTarget, out Health targetHealth) &&
                    targetHealth.CurrentValue > 0)
                {
                    float distSqToTarget = math.distancesq(myPos, targetTransform.Position);
                    // 고착화: 원래 범위의 1.3배까지 타겟 유지
                    if (distSqToTarget <= hysteresisRangeSq)
                    {
                        // 유효 타겟 → 위치만 업데이트, return 하지 않음
                        // needImmediateSearch는 false 유지 → 시간 분할 적용됨
                        aggroTarget.ValueRW.LastTargetPosition = targetTransform.Position;

                        if (math.distancesq(goal.ValueRO.Destination, targetTransform.Position) > DestinationThresholdSq)
                        {
                            goal.ValueRW.Destination = targetTransform.Position;
                            goal.ValueRW.IsPathDirty = true;
                        }
                        waypointsEnabled.ValueRW = true;

                        // Intent 업데이트 (기존 로직 유지)
                        if (currentIntent != Intent.Attack)
                        {
                            intent.ValueRW.State = Intent.Attack;
                            intent.ValueRW.TargetEntity = currentTarget;
                        }
                        // return 제거 → 시간 분할 체크로 진행
                    }
                    else
                    {
                        needImmediateSearch = true;
                    }
                }
                else
                {
                    // 타겟 무효화됨 - 즉시 탐색 필요
                    needImmediateSearch = true;
                }
            }

            // ---------------------------------------------------------
            // 3. 시간 분할 (유효 타겟 있는 경우에도 도달 가능)
            // ---------------------------------------------------------
            if (!needImmediateSearch)
            {
                uint frameSlice = FrameCount % TimeSliceDivisor;
                bool isMySearchFrame = ((uint)entity.Index % TimeSliceDivisor) == frameSlice;
                if (!isMySearchFrame)
                    return;  // 기존 타겟 유지한 채 리턴
            }

            // ---------------------------------------------------------
            // 4. 새로운 타겟 탐색 (공간 분할 기반)
            // ---------------------------------------------------------
            Entity bestTarget = Entity.Null;
            float bestDistSq = float.MaxValue;
            float3 bestTargetPos = float3.zero;

            int searchRadius = (int)math.ceil(visionRange.ValueRO.Value / CellSize);

            for (int x = -searchRadius; x <= searchRadius; x++)
            {
                for (int z = -searchRadius; z <= searchRadius; z++)
                {
                    int hash = SpatialHashUtility.GetCellHash(myPos, x, z, CellSize);

                    if (TargetingMap.TryGetFirstValue(hash, out SpatialTargetEntry candidate, out var it))
                    {
                        do
                        {
                            // 자기 자신 제외
                            if (candidate.Entity == entity) continue;

                            // 유닛은 teamId가 0 또는 양수, 적은 음수 (또는 다른 팀)
                            // 여기서는 적 엔티티(EnemyTag)만 공격해야 함
                            // SpatialTargetEntry에 TeamId가 있으므로 적(음수 teamId)만 필터링
                            // EnemyTag 체크는 비용이 크므로 TeamId로 판단
                            // 일반적으로 유닛 Team = 0 or 1, 적 Team = -1
                            if (candidate.TeamId >= 0) continue; // 적이 아니면 스킵

                            float distSq = math.distancesq(myPos, candidate.Position);

                            if (distSq <= visionRangeSq && distSq < bestDistSq)
                            {
                                bestDistSq = distSq;
                                bestTarget = candidate.Entity;
                                bestTargetPos = candidate.Position;
                            }

                        } while (TargetingMap.TryGetNextValue(out candidate, ref it));
                    }
                }
            }

            // ---------------------------------------------------------
            // 5. 결과 적용
            // ---------------------------------------------------------
            if (bestTarget != Entity.Null)
            {
                // 새 타겟이 기존 타겟보다 가깝거나, 기존 타겟이 없는 경우에만 전환
                bool shouldSwitch = currentTarget == Entity.Null
                    || bestDistSq < math.distancesq(myPos, aggroTarget.ValueRO.LastTargetPosition);
                if (shouldSwitch)
                {
                    intent.ValueRW.State = Intent.Attack;
                    intent.ValueRW.TargetEntity = bestTarget;

                    aggroTarget.ValueRW.TargetEntity = bestTarget;
                    aggroTarget.ValueRW.LastTargetPosition = bestTargetPos;

                    aggroLock.ValueRW.LockedTarget = bestTarget;
                    aggroLock.ValueRW.RemainingLockTime = aggroLock.ValueRO.LockDuration;

                    goal.ValueRW.Destination = bestTargetPos;
                    goal.ValueRW.IsPathDirty = true;
                    waypointsEnabled.ValueRW = true;
                }
            }
            else if (currentIntent == Intent.Idle && currentTarget != Entity.Null)
            {
                aggroTarget.ValueRW.TargetEntity = Entity.Null;
                intent.ValueRW.TargetEntity = Entity.Null;
            }
        }
    }

    // =========================================================================
    // 배회 전용 Job (타겟 없을 때)
    // =========================================================================

    [BurstCompile]
    [WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)]
    public partial struct EnemyWanderOnlyJob : IJobEntity
    {
        [ReadOnly] public GridSettings GridSettings;
        public float ElapsedTime;
        public uint FrameCount;

        // 위치 정체 체크 간격 (초)
        private const float StuckCheckInterval = 3.0f;
        // stuck 판정 이동 거리 (미터)
        private const float StuckThreshold = 2.0f;

        public void Execute(
            Entity entity,
            RefRO<LocalTransform> myTransform,
            RefRW<AggroTarget> target,
            RefRW<EnemyState> enemyState,
            RefRW<MovementGoal> goal,
            EnabledRefRW<MovementWaypoints> waypointsEnabled,
            in EnemyTag enemyTag)
        {
            target.ValueRW.TargetEntity = Entity.Null;
            float3 myPos = myTransform.ValueRO.Position;

            // stuck 감지: 배회 중 위치 정체 시 재배회
            bool forceRefresh = false;
            if (enemyState.ValueRO.CurrentState == EnemyContext.Wandering)
            {
                if (ElapsedTime - goal.ValueRO.LastPositionCheckTime >= StuckCheckInterval)
                {
                    float movedDistance = math.distance(myPos, goal.ValueRO.LastPositionCheck);
                    if (movedDistance < StuckThreshold)
                    {
                        forceRefresh = true;
                    }
                    goal.ValueRW.LastPositionCheckTime = ElapsedTime;
                    goal.ValueRW.LastPositionCheck = myPos;
                }
            }

            bool needNewWanderTarget = enemyState.ValueRO.CurrentState != EnemyContext.Wandering
                                       || !waypointsEnabled.ValueRO
                                       || forceRefresh;

            if (needNewWanderTarget)
            {
                // 황금비 매직 넘버로 비트 패턴을 잘 섞어줌
                uint seed = (uint)entity.Index ^ (FrameCount * 0x9E3779B9) ^ (uint)(ElapsedTime * 1000);
                var random = Random.CreateFromIndex(seed);

                float2 mapMin = GridSettings.GridOrigin;
                float2 mapMax = mapMin + new float2(
                    GridSettings.GridSize.x * GridSettings.CellSize,
                    GridSettings.GridSize.y * GridSettings.CellSize
                );

                float3 wanderDest = new float3(
                    random.NextFloat(mapMin.x + 5f, mapMax.x - 5f),
                    myPos.y,
                    random.NextFloat(mapMin.y + 5f, mapMax.y - 5f)
                );

                goal.ValueRW.Destination = wanderDest;
                goal.ValueRW.IsPathDirty = true;
                goal.ValueRW.DestinationSetTime = ElapsedTime;
                goal.ValueRW.LastPositionCheckTime = ElapsedTime;
                goal.ValueRW.LastPositionCheck = myPos;
                waypointsEnabled.ValueRW = true;
            }

            enemyState.ValueRW.CurrentState = EnemyContext.Wandering;
        }
    }
}
