using Unity.Burst;
using Unity.Entities;
using Unity.Collections;
using Unity.NetCode;
using Shared;

/// <summary>
/// 2단계 사망 처리:
/// DeathDetectionJob: Health <= 0 감지 → Dying 상태 설정 + DeathTimer 부착 + 인구 반환
/// DeathTimerJob: DeathTimer 카운트다운 → 만료 시 엔티티 파괴
/// 건물 등 비유닛/비적 엔티티는 즉시 파괴한다.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[BurstCompile]
public partial struct ServerDeathSystem : ISystem
{
    [ReadOnly] private ComponentLookup<ProductionCost> _productionCostLookup;
    [ReadOnly] private ComponentLookup<GhostOwner> _ghostOwnerLookup;
    [ReadOnly] private ComponentLookup<UnitActionState> _unitActionStateLookup;
    [ReadOnly] private ComponentLookup<EnemyState> _enemyStateLookup;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();

        _productionCostLookup = state.GetComponentLookup<ProductionCost>(true);
        _ghostOwnerLookup = state.GetComponentLookup<GhostOwner>(true);
        _unitActionStateLookup = state.GetComponentLookup<UnitActionState>(true);
        _enemyStateLookup = state.GetComponentLookup<EnemyState>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        _productionCostLookup.Update(ref state);
        _ghostOwnerLookup.Update(ref state);
        _unitActionStateLookup.Update(ref state);
        _enemyStateLookup.Update(ref state);

        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();

        float deathDuration = SystemAPI.TryGetSingleton<GameSettings>(out var gs) && gs.DeathDuration > 0f
            ? gs.DeathDuration
            : 0.5f;

        // NetworkId -> UserEconomy 매핑 생성
        var networkIdToEconomyEntity = new NativeParallelHashMap<int, Entity>(16, Allocator.TempJob);
        foreach (var (ghostOwner, entity) in SystemAPI.Query<RefRO<GhostOwner>>()
                     .WithAll<UserEconomyTag>()
                     .WithEntityAccess())
        {
            networkIdToEconomyEntity.TryAdd(ghostOwner.ValueRO.NetworkId, entity);
        }

        // 1단계: Health <= 0 감지 → Dying 상태 설정 + DeathTimer 부착
        var detectionEcb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();
        state.Dependency = new DeathDetectionJob
        {
            Ecb = detectionEcb,
            ProductionCostLookup = _productionCostLookup,
            GhostOwnerLookup = _ghostOwnerLookup,
            NetworkIdToEconomyEntity = networkIdToEconomyEntity,
            UnitActionStateLookup = _unitActionStateLookup,
            EnemyStateLookup = _enemyStateLookup,
            DeathDuration = deathDuration
        }.ScheduleParallel(state.Dependency);

        // 2단계: DeathTimer 카운트다운 → 만료 시 파괴
        var timerEcb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();
        float deltaTime = SystemAPI.Time.DeltaTime;
        state.Dependency = new DeathTimerJob
        {
            Ecb = timerEcb,
            DeltaTime = deltaTime
        }.ScheduleParallel(state.Dependency);

        state.Dependency = networkIdToEconomyEntity.Dispose(state.Dependency);
    }
}

/// <summary>
/// Health <= 0이고 아직 DeathTimer가 없는 엔티티를 감지하여
/// Dying 상태 설정 + DeathTimer 부착 (유닛/적) 또는 즉시 파괴 (건물 등).
/// </summary>
[BurstCompile]
[WithNone(typeof(DeathTimer))]
public partial struct DeathDetectionJob : IJobEntity
{
    public EntityCommandBuffer.ParallelWriter Ecb;
    [ReadOnly] public ComponentLookup<ProductionCost> ProductionCostLookup;
    [ReadOnly] public ComponentLookup<GhostOwner> GhostOwnerLookup;
    [ReadOnly] public NativeParallelHashMap<int, Entity> NetworkIdToEconomyEntity;
    [ReadOnly] public ComponentLookup<UnitActionState> UnitActionStateLookup;
    [ReadOnly] public ComponentLookup<EnemyState> EnemyStateLookup;
    public float DeathDuration;

    private void Execute([EntityIndexInQuery] int sortKey, Entity entity, ref Health health)
    {
        if (health.CurrentValue > 0) return;

        // 유닛: Dying 상태 설정 + DeathTimer 부착 + 인구 반환
        if (UnitActionStateLookup.TryGetComponent(entity, out var unitAction))
        {
            if (unitAction.State == Action.Dying || unitAction.State == Action.Dead) return;

            Ecb.SetComponent(sortKey, entity, new UnitActionState { State = Action.Dying });
            Ecb.AddComponent(sortKey, entity, new DeathTimer { RemainingTime = DeathDuration });
            ReturnPopulation(sortKey, entity);
            return;
        }

        // 적: Dying 상태 설정 + DeathTimer 부착
        if (EnemyStateLookup.TryGetComponent(entity, out var enemyState))
        {
            if (enemyState.CurrentState == EnemyContext.Dying || enemyState.CurrentState == EnemyContext.Dead) return;

            Ecb.SetComponent(sortKey, entity, new EnemyState { CurrentState = EnemyContext.Dying });
            Ecb.AddComponent(sortKey, entity, new DeathTimer { RemainingTime = DeathDuration });
            return;
        }

        // 기타 엔티티 (건물 등): 즉시 파괴
        ReturnPopulation(sortKey, entity);
        Ecb.DestroyEntity(sortKey, entity);
    }

    private void ReturnPopulation(int sortKey, Entity entity)
    {
        if (GhostOwnerLookup.HasComponent(entity) && ProductionCostLookup.HasComponent(entity))
        {
            int ownerId = GhostOwnerLookup[entity].NetworkId;
            int popCost = ProductionCostLookup[entity].PopulationCost;

            if (popCost > 0 && NetworkIdToEconomyEntity.TryGetValue(ownerId, out Entity economyEntity))
            {
                Ecb.AppendToBuffer(sortKey, economyEntity, new PopulationEvent { Delta = -popCost });
            }
        }
    }
}

/// <summary>
/// DeathTimer가 부착된 엔티티의 RemainingTime을 매 프레임 감소시키고,
/// 0 이하가 되면 엔티티를 파괴한다.
/// </summary>
[BurstCompile]
public partial struct DeathTimerJob : IJobEntity
{
    public EntityCommandBuffer.ParallelWriter Ecb;
    public float DeltaTime;

    private void Execute([EntityIndexInQuery] int sortKey, Entity entity, ref DeathTimer deathTimer)
    {
        deathTimer.RemainingTime -= DeltaTime;

        if (deathTimer.RemainingTime <= 0f)
        {
            Ecb.DestroyEntity(sortKey, entity);
        }
    }
}
