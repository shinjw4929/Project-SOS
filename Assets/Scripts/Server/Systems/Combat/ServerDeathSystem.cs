using Unity.Burst;
using Unity.Entities;
using Unity.Collections;
using Unity.NetCode;
using Shared;

/// <summary>
/// 2단계 사망 처리:
/// 1프레임째: Health <= 0 감지 → Dying 상태 설정 + 인구 반환 (Ghost 스냅샷에 Dying 포함)
/// 2프레임째: Dying 상태 → 엔티티 파괴
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
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();

        // NetworkId -> UserEconomy 매핑 생성
        var networkIdToEconomyEntity = new NativeParallelHashMap<int, Entity>(16, Allocator.TempJob);
        foreach (var (ghostOwner, entity) in SystemAPI.Query<RefRO<GhostOwner>>()
                     .WithAll<UserEconomyTag>()
                     .WithEntityAccess())
        {
            networkIdToEconomyEntity.TryAdd(ghostOwner.ValueRO.NetworkId, entity);
        }

        new ServerDeathJob
        {
            Ecb = ecb,
            ProductionCostLookup = _productionCostLookup,
            GhostOwnerLookup = _ghostOwnerLookup,
            NetworkIdToEconomyEntity = networkIdToEconomyEntity,
            UnitActionStateLookup = _unitActionStateLookup,
            EnemyStateLookup = _enemyStateLookup
        }.ScheduleParallel();

        state.Dependency = networkIdToEconomyEntity.Dispose(state.Dependency);
    }
}

[BurstCompile]
public partial struct ServerDeathJob : IJobEntity
{
    public EntityCommandBuffer.ParallelWriter Ecb;
    [ReadOnly] public ComponentLookup<ProductionCost> ProductionCostLookup;
    [ReadOnly] public ComponentLookup<GhostOwner> GhostOwnerLookup;
    [ReadOnly] public NativeParallelHashMap<int, Entity> NetworkIdToEconomyEntity;
    [ReadOnly] public ComponentLookup<UnitActionState> UnitActionStateLookup;
    [ReadOnly] public ComponentLookup<EnemyState> EnemyStateLookup;

    private void Execute([EntityIndexInQuery] int sortKey, Entity entity, ref Health health)
    {
        if (health.CurrentValue > 0) return;

        // 유닛: 2단계 사망
        if (UnitActionStateLookup.TryGetComponent(entity, out var unitAction))
        {
            if (unitAction.State != Action.Dying && unitAction.State != Action.Dead)
            {
                // 1단계: Dying 상태 설정 + 인구 반환
                Ecb.SetComponent(sortKey, entity, new UnitActionState { State = Action.Dying });
                ReturnPopulation(sortKey, entity);
                return;
            }
            // 2단계: 파괴
            Ecb.DestroyEntity(sortKey, entity);
            return;
        }

        // 적: 2단계 사망
        if (EnemyStateLookup.TryGetComponent(entity, out var enemyState))
        {
            if (enemyState.CurrentState != EnemyContext.Dying && enemyState.CurrentState != EnemyContext.Dead)
            {
                // 1단계: Dying 상태 설정
                Ecb.SetComponent(sortKey, entity, new EnemyState { CurrentState = EnemyContext.Dying });
                return;
            }
            // 2단계: 파괴
            Ecb.DestroyEntity(sortKey, entity);
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
