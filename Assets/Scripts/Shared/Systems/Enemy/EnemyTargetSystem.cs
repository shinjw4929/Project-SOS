using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Collections;
using Unity.Burst;
using Shared; // UnitTag, StructureTag, Team, EnemyTarget, EnemyFollowConfig

[UpdateInGroup(typeof(SimulationSystemGroup))]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)] // 서버 전용
[BurstCompile]
public partial struct EnemyTargetSystem : ISystem
{
    private ComponentLookup<LocalTransform> _transformLookup;
    private EntityQuery _potentialTargetQuery;

    public void OnCreate(ref SystemState state)
    {
        _transformLookup = state.GetComponentLookup<LocalTransform>(isReadOnly: true);

        // [핵심] 복합 조건 쿼리 생성 (Any 사용)
        // 조건: (LocalTransform AND Team) AND (UnitTag OR StructureTag) AND NOT EnemyTag
        var queryDesc = new EntityQueryDesc
        {
            All = new ComponentType[]
            {
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<Team>()
            },
            Any = new ComponentType[]
            {
                ComponentType.ReadOnly<UnitTag>(),     // 유닛
                ComponentType.ReadOnly<StructureTag>() // 건물
            },
            None = new ComponentType[]
            {
                ComponentType.ReadOnly<EnemyTag>()     // Enemy는 타겟 후보에서 제외
            }
        };
        _potentialTargetQuery = state.EntityManager.CreateEntityQuery(queryDesc);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        _transformLookup.Update(ref state);

        // 1. 타겟 후보군(아군 유닛/건물) 데이터 스냅샷
        // Job이 완료되면 자동으로 메모리 해제되도록 TempJob 사용
        var potentialTargets = _potentialTargetQuery.ToEntityArray(Allocator.TempJob);
        var targetTransforms = _potentialTargetQuery.ToComponentDataArray<LocalTransform>(Allocator.TempJob);
        var targetTeams = _potentialTargetQuery.ToComponentDataArray<Team>(Allocator.TempJob);

        // 2. Job 예약
        var job = new EnemyTargetJob
        {
            PotentialTargets = potentialTargets,
            TargetTransforms = targetTransforms,
            TargetTeams = targetTeams,
            TransformLookup = _transformLookup
        };

        state.Dependency = job.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    public partial struct EnemyTargetJob : IJobEntity
    {
        // [DeallocateOnJobCompletion]: Job이 끝나면 이 배열들을 자동으로 Dispose 함 (메모리 누수 방지)
        [ReadOnly] [DeallocateOnJobCompletion] public NativeArray<Entity> PotentialTargets;
        [ReadOnly] [DeallocateOnJobCompletion] public NativeArray<LocalTransform> TargetTransforms;
        [ReadOnly] [DeallocateOnJobCompletion] public NativeArray<Team> TargetTeams;

        [ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;

        // 목적지 변경 역치 (1m 이상 차이나야 경로 재계산)
        private const float DestinationThresholdSq = 1.0f;

        // 적군(Enemy)을 찾아서 실행
        // 필터: EnemyTag가 있는 엔티티만 처리
        public void Execute(
            Entity entity,
            RefRO<LocalTransform> myTransform,
            RefRW<AggroTarget> target,
            RefRW<EnemyState> enemyState,
            RefRO<EnemyChaseDistance> chaseDistance,
            RefRO<Team> myTeam,
            RefRW<MovementGoal> goal,  // NavMesh 경로 탐색용
            in EnemyTag enemyTag)
        {
            float3 myPos = myTransform.ValueRO.Position;
            bool needNewTarget = false;
            float loseDistSq = chaseDistance.ValueRO.LoseTargetDistance * chaseDistance.ValueRO.LoseTargetDistance;

            // ---------------------------------------------------------
            // 1. 현재 타겟 유효성 검사 (TargetEntity != Entity.Null로 판단)
            // ---------------------------------------------------------
            Entity currentTarget = target.ValueRO.TargetEntity;
            if (currentTarget == Entity.Null)
            {
                needNewTarget = true;
            }
            else
            {
                // TryGetComponent 패턴 적용 - 중복 조회 방지
                if (!TransformLookup.TryGetComponent(currentTarget, out LocalTransform targetTransform))
                {
                    // 타겟이 파괴됨
                    needNewTarget = true;
                }
                else
                {
                    float3 targetPos = targetTransform.Position;
                    float distSq = math.distancesq(myPos, targetPos);

                    // 추적 포기 거리보다 멀어지면 타겟 해제
                    if (distSq > loseDistSq)
                    {
                        needNewTarget = true;
                    }
                    else
                    {
                        // 타겟이 유효하면 마지막 위치 갱신 (이동 시스템에서 사용)
                        target.ValueRW.LastTargetPosition = targetPos;

                        // [NavMesh] 목적지 갱신 (역치 기반 - 불필요한 경로 재계산 방지)
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
            // 2. 새로운 타겟 탐색
            // ---------------------------------------------------------
            Entity bestTarget = Entity.Null;
            float bestDistSq = float.MaxValue;

            // 참고: Config에 'AggroRange'(인식 범위)가 없으므로
            // 일단 'LoseTargetDistance'를 인식 범위로도 사용합니다.
            float searchRadiusSq = loseDistSq;

            for (int i = 0; i < PotentialTargets.Length; i++)
            {
                Entity candidate = PotentialTargets[i];

                // 자기 자신 제외
                if (candidate == entity) continue;

                // 같은 팀이면 공격 안 함
                if (TargetTeams[i].teamId == myTeam.ValueRO.teamId) continue;

                float3 targetPos = TargetTransforms[i].Position;
                float distSq = math.distancesq(myPos, targetPos);

                // 인식 범위 내에 있고, 가장 가까운 적 선택
                if (distSq < searchRadiusSq && distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestTarget = candidate;
                }
            }

            // ---------------------------------------------------------
            // 3. 결과 적용 + EnemyState 업데이트 + MovementGoal 갱신
            // ---------------------------------------------------------
            Entity previousTarget = target.ValueRO.TargetEntity;

            if (bestTarget != Entity.Null)
            {
                target.ValueRW.TargetEntity = bestTarget;

                // TryGetComponent로 위치 조회
                if (TransformLookup.TryGetComponent(bestTarget, out LocalTransform bestTargetTransform))
                {
                    float3 targetPos = bestTargetTransform.Position;
                    target.ValueRW.LastTargetPosition = targetPos;

                    // [NavMesh] 타겟이 변경되었거나 거리 역치 초과 시 경로 재계산
                    bool targetChanged = previousTarget != bestTarget;
                    float3 currentDest = goal.ValueRO.Destination;

                    if (targetChanged || math.distancesq(currentDest, targetPos) > DestinationThresholdSq)
                    {
                        goal.ValueRW.Destination = targetPos;
                        goal.ValueRW.IsPathDirty = true;
                    }
                }

                // EnemyState를 Chasing으로 변경
                enemyState.ValueRW.CurrentState = EnemyContext.Chasing;
            }
            else
            {
                target.ValueRW.TargetEntity = Entity.Null;

                // EnemyState를 Idle로 변경
                enemyState.ValueRW.CurrentState = EnemyContext.Idle;
            }
        }
    }
}
