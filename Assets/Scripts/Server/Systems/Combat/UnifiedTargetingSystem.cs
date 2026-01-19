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

        public void Execute(
            Entity entity,
            RefRO<LocalTransform> myTransform,
            RefRW<AggroTarget> target,
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
                        }
                    }
                }
            }

            if (!needNewTarget) return;

            // ---------------------------------------------------------
            // 2. 새로운 타겟 탐색 (공간 분할 기반)
            // ---------------------------------------------------------
            Entity bestTarget = Entity.Null;
            float bestDistSq = float.MaxValue;
            float3 bestTargetPos = float3.zero;
            float searchRadiusSq = loseDistSq;

            // 인접 9개 셀만 순회
            for (int x = -1; x <= 1; x++)
            {
                for (int z = -1; z <= 1; z++)
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
                }

                enemyState.ValueRW.CurrentState = EnemyContext.Chasing;
            }
            else
            {
                target.ValueRW.TargetEntity = Entity.Null;

                // ---------------------------------------------------------
                // 배회 로직
                // ---------------------------------------------------------
                bool needNewWanderTarget = enemyState.ValueRO.CurrentState != EnemyContext.Wandering
                                           || !waypointsEnabled.ValueRO;

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
            RefRW<MovementGoal> goal,
            EnabledRefRW<MovementWaypoints> waypointsEnabled)
        {
            // ---------------------------------------------------------
            // 1. 자동 타겟팅 활성화 조건 체크
            // ---------------------------------------------------------
            Intent currentIntent = intent.ValueRO.State;
            if (currentIntent != Intent.Idle && currentIntent != Intent.AttackMove)
                return;

            float3 myPos = myTransform.ValueRO.Position;
            float visionRangeSq = visionRange.ValueRO.Value * visionRange.ValueRO.Value;
            // 타겟 고착화 범위
            float hysteresisRangeSq = visionRangeSq * (HysteresisMultiplier * HysteresisMultiplier);

            // ---------------------------------------------------------
            // 2. 현재 타겟 유효성 검사 (Early Exit + 고착화)
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
                        if (currentIntent != Intent.Attack)
                        {
                            intent.ValueRW.State = Intent.Attack;
                            intent.ValueRW.TargetEntity = currentTarget;
                        }

                        aggroTarget.ValueRW.LastTargetPosition = targetTransform.Position;

                        if (math.distancesq(goal.ValueRO.Destination, targetTransform.Position) > DestinationThresholdSq)
                        {
                            goal.ValueRW.Destination = targetTransform.Position;
                            goal.ValueRW.IsPathDirty = true;
                        }
                        waypointsEnabled.ValueRW = true;
                        return; // Early Exit
                    }
                }
                // 타겟 무효화됨 - 즉시 탐색 필요
                needImmediateSearch = true;
            }

            // ---------------------------------------------------------
            // 3. 시간 분할: 타겟 없으면 즉시 탐색, 있으면 4프레임에 1번
            // ---------------------------------------------------------
            if (!needImmediateSearch)
            {
                uint frameSlice = FrameCount % TimeSliceDivisor;
                bool isMySearchFrame = ((uint)entity.Index % TimeSliceDivisor) == frameSlice;
                if (!isMySearchFrame)
                    return;
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
                intent.ValueRW.State = Intent.Attack;
                intent.ValueRW.TargetEntity = bestTarget;

                aggroTarget.ValueRW.TargetEntity = bestTarget;
                aggroTarget.ValueRW.LastTargetPosition = bestTargetPos;

                goal.ValueRW.Destination = bestTargetPos;
                goal.ValueRW.IsPathDirty = true;
                waypointsEnabled.ValueRW = true;
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

            bool needNewWanderTarget = enemyState.ValueRO.CurrentState != EnemyContext.Wandering
                                       || !waypointsEnabled.ValueRO;

            if (needNewWanderTarget)
            {
                // 황금비 매직 넘버로 비트 패턴을 잘 섞어줌
                uint seed = (uint)entity.Index ^ (FrameCount * 0x9E3779B9) ^ (uint)(ElapsedTime * 1000);
                var random = Random.CreateFromIndex(seed);
                float3 myPos = myTransform.ValueRO.Position;

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
                waypointsEnabled.ValueRW = true;
            }

            enemyState.ValueRW.CurrentState = EnemyContext.Wandering;
        }
    }
}
