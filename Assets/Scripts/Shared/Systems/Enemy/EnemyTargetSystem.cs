using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Collections;
using Unity.Burst;
using Shared;

/// <summary>
/// 적(Enemy) 타겟팅 시스템
/// <para>공간 분할(Spatial Partitioning)을 활용하여 O(N×M) → O(N×K)로 최적화</para>
/// <para>- ToEntityArray/ToComponentDataArray 제거로 메모리 할당 최소화</para>
/// <para>- 인접 9개 셀만 탐색하여 거리 계산 횟수 대폭 감소</para>
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[BurstCompile]
public partial struct EnemyTargetSystem : ISystem
{
    private ComponentLookup<LocalTransform> _transformLookup;
    private EntityQuery _potentialTargetQuery;

    // 공간 분할 셀 크기 (LoseTargetDistance의 절반 정도가 적절)
    private const float CellSize = 10.0f;

    public void OnCreate(ref SystemState state)
    {
        _transformLookup = state.GetComponentLookup<LocalTransform>(isReadOnly: true);

        // GridSettings가 있어야 시스템 실행
        state.RequireForUpdate<GridSettings>();

        // [핵심] 복합 조건 쿼리 생성 (Any 사용)
        // 조건: (LocalTransform AND Team) AND (UnitTag OR StructureTag) AND NOT (EnemyTag OR WallTag)
        var queryDesc = new EntityQueryDesc
        {
            All = new ComponentType[]
            {
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<Team>()
            },
            Any = new ComponentType[]
            {
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<StructureTag>()
            },
            None = new ComponentType[]
            {
                ComponentType.ReadOnly<EnemyTag>(),
                ComponentType.ReadOnly<WallTag>()
            }
        };
        _potentialTargetQuery = state.EntityManager.CreateEntityQuery(queryDesc);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        _transformLookup.Update(ref state);

        // GridSettings 조회 (배회용)
        var gridSettings = SystemAPI.GetSingleton<GridSettings>();
        float elapsedTime = (float)SystemAPI.Time.ElapsedTime;

        // --- 공간 분할 맵 생성 ---
        int targetCount = _potentialTargetQuery.CalculateEntityCount();
        if (targetCount == 0)
        {
            // 타겟이 없으면 모든 적을 배회 상태로 전환
            var wanderOnlyJob = new EnemyWanderOnlyJob
            {
                GridSettings = gridSettings,
                ElapsedTime = elapsedTime
            };
            state.Dependency = wanderOnlyJob.ScheduleParallel(state.Dependency);
            return;
        }

        var targetSpatialMap = new NativeParallelMultiHashMap<int, TargetInfo>(targetCount, Allocator.TempJob);

        // 1. 타겟 후보를 공간 분할 맵에 등록 (Job 속성으로 쿼리 필터 적용)
        var buildMapJob = new BuildTargetSpatialMapJob
        {
            SpatialMap = targetSpatialMap.AsParallelWriter(),
            CellSize = CellSize
        };
        var buildHandle = buildMapJob.ScheduleParallel(state.Dependency);

        // 2. 적 타겟팅 Job 실행
        var targetJob = new EnemyTargetWithSpatialJob
        {
            SpatialMap = targetSpatialMap,
            TransformLookup = _transformLookup,
            GridSettings = gridSettings,
            ElapsedTime = elapsedTime,
            CellSize = CellSize
        };
        state.Dependency = targetJob.ScheduleParallel(buildHandle);

        // 3. 메모리 해제 예약
        targetSpatialMap.Dispose(state.Dependency);
    }

    // =========================================================================
    // Helper Structs
    // =========================================================================

    /// <summary>
    /// 타겟 정보 (공간 분할 맵 엔트리)
    /// </summary>
    public struct TargetInfo
    {
        public Entity Entity;
        public float3 Position;
        public int TeamId;
    }

    /// <summary>
    /// 셀 해시 계산 (PredictedMovementSystem과 동일한 방식)
    /// </summary>
    public static int GetCellHash(float3 pos, float cellSize)
    {
        return (int)math.hash(new int2((int)(pos.x / cellSize), (int)(pos.z / cellSize)));
    }

    public static int GetCellHash(float3 pos, int xOff, int zOff, float cellSize)
    {
        return (int)math.hash(new int2((int)(pos.x / cellSize) + xOff, (int)(pos.z / cellSize) + zOff));
    }

    // =========================================================================
    // Job 1: 타겟 후보를 공간 분할 맵에 등록
    // =========================================================================

    [BurstCompile]
    [WithAll(typeof(LocalTransform), typeof(Team))]
    [WithAny(typeof(UnitTag), typeof(StructureTag))]
    [WithNone(typeof(EnemyTag), typeof(WallTag))]
    public partial struct BuildTargetSpatialMapJob : IJobEntity
    {
        public NativeParallelMultiHashMap<int, TargetInfo>.ParallelWriter SpatialMap;
        public float CellSize;

        public void Execute(Entity entity, in LocalTransform transform, in Team team)
        {
            int hash = GetCellHash(transform.Position, CellSize);
            SpatialMap.Add(hash, new TargetInfo
            {
                Entity = entity,
                Position = transform.Position,
                TeamId = team.teamId
            });
        }
    }

    // =========================================================================
    // Job 2: 적 타겟팅 (공간 분할 기반)
    // =========================================================================

    [BurstCompile]
    [WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)]
    public partial struct EnemyTargetWithSpatialJob : IJobEntity
    {
        [ReadOnly] public NativeParallelMultiHashMap<int, TargetInfo> SpatialMap;
        [ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;
        [ReadOnly] public GridSettings GridSettings;
        public float ElapsedTime;
        public float CellSize;

        // 목적지 변경 역치 (1m 이상 차이나야 경로 재계산)
        private const float DestinationThresholdSq = 1.0f;

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

            // ---------------------------------------------------------
            // 1. 현재 타겟 유효성 검사
            // ---------------------------------------------------------
            Entity currentTarget = target.ValueRO.TargetEntity;
            if (currentTarget == Entity.Null)
            {
                needNewTarget = true;
            }
            else
            {
                if (!TransformLookup.TryGetComponent(currentTarget, out LocalTransform targetTransform))
                {
                    needNewTarget = true;
                }
                else
                {
                    float3 targetPos = targetTransform.Position;
                    float distSq = math.distancesq(myPos, targetPos);

                    if (distSq > loseDistSq)
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
            // 2. 새로운 타겟 탐색 (공간 분할 기반 - 인접 9개 셀만 순회)
            // ---------------------------------------------------------
            Entity bestTarget = Entity.Null;
            float bestDistSq = float.MaxValue;
            float3 bestTargetPos = float3.zero;
            float searchRadiusSq = loseDistSq;

            // 인접 9개 셀만 순회 (중심 + 8방향)
            for (int x = -1; x <= 1; x++)
            {
                for (int z = -1; z <= 1; z++)
                {
                    int hash = GetCellHash(myPos, x, z, CellSize);

                    if (SpatialMap.TryGetFirstValue(hash, out TargetInfo candidate, out var it))
                    {
                        do
                        {
                            // 자기 자신 제외
                            if (candidate.Entity == entity) continue;

                            // 같은 팀이면 공격 안 함
                            if (candidate.TeamId == myTeam.ValueRO.teamId) continue;

                            float distSq = math.distancesq(myPos, candidate.Position);

                            // 인식 범위 내에 있고, 가장 가까운 적 선택
                            if (distSq < searchRadiusSq && distSq < bestDistSq)
                            {
                                bestDistSq = distSq;
                                bestTarget = candidate.Entity;
                                bestTargetPos = candidate.Position;
                            }

                        } while (SpatialMap.TryGetNextValue(out candidate, ref it));
                    }
                }
            }

            // ---------------------------------------------------------
            // 3. 결과 적용 + EnemyState 업데이트 + MovementGoal 갱신
            // ---------------------------------------------------------
            Entity previousTarget = target.ValueRO.TargetEntity;

            if (bestTarget != Entity.Null)
            {
                target.ValueRW.TargetEntity = bestTarget;
                target.ValueRW.LastTargetPosition = bestTargetPos;

                // [NavMesh] 타겟이 변경되었거나 거리 역치 초과 시 경로 재계산
                bool targetChanged = previousTarget != bestTarget;
                float3 currentDest = goal.ValueRO.Destination;

                if (targetChanged || math.distancesq(currentDest, bestTargetPos) > DestinationThresholdSq)
                {
                    goal.ValueRW.Destination = bestTargetPos;
                    goal.ValueRW.IsPathDirty = true;
                }

                // EnemyState를 Chasing으로 변경
                enemyState.ValueRW.CurrentState = EnemyContext.Chasing;
            }
            else
            {
                target.ValueRW.TargetEntity = Entity.Null;

                // ---------------------------------------------------------
                // 배회 로직: 타겟 없으면 맵 전역을 랜덤하게 배회
                // ---------------------------------------------------------
                bool needNewWanderTarget = enemyState.ValueRO.CurrentState != EnemyContext.Wandering
                                           || !waypointsEnabled.ValueRO;

                if (needNewWanderTarget)
                {
                    var random = Random.CreateFromIndex((uint)(entity.Index + ElapsedTime * 1000));

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
    // Job 3: 타겟 없을 때 배회 전용 Job
    // =========================================================================

    [BurstCompile]
    [WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)]
    public partial struct EnemyWanderOnlyJob : IJobEntity
    {
        [ReadOnly] public GridSettings GridSettings;
        public float ElapsedTime;

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
                var random = Random.CreateFromIndex((uint)(entity.Index + ElapsedTime * 1000));
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
