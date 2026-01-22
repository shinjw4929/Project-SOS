using Unity.Burst;
using Unity.Entities;
using Unity.Collections;
using Unity.NetCode;
using Shared;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[BurstCompile]
public partial struct ServerDeathSystem : ISystem
{
    [ReadOnly] private ComponentLookup<ProductionCost> _productionCostLookup;
    [ReadOnly] private ComponentLookup<GhostOwner> _ghostOwnerLookup;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();

        _productionCostLookup = state.GetComponentLookup<ProductionCost>(true);
        _ghostOwnerLookup = state.GetComponentLookup<GhostOwner>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        _productionCostLookup.Update(ref state);
        _ghostOwnerLookup.Update(ref state);

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
            NetworkIdToEconomyEntity = networkIdToEconomyEntity
        }.ScheduleParallel();

        // TempJob은 다음 프레임까지 유효하므로 의존성 완료 후 해제할 필요 없음
        // (CompleteDependency 없이 자동으로 처리됨)
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

    private void Execute([EntityIndexInQuery] int sortKey, Entity entity, ref Health health)
    {
        if (health.CurrentValue <= 0)
        {
            // 인구수 반환 (DestroyEntity 전에 데이터 읽기)
            if (GhostOwnerLookup.HasComponent(entity) && ProductionCostLookup.HasComponent(entity))
            {
                int ownerId = GhostOwnerLookup[entity].NetworkId;
                int popCost = ProductionCostLookup[entity].PopulationCost;

                if (popCost > 0 && NetworkIdToEconomyEntity.TryGetValue(ownerId, out Entity economyEntity))
                {
                    // Thread-Safe: ECB.AppendToBuffer 사용 (음수 Delta)
                    Ecb.AppendToBuffer(sortKey, economyEntity, new PopulationEvent { Delta = -popCost });
                }
            }

            Ecb.DestroyEntity(sortKey, entity);
        }
    }
}
