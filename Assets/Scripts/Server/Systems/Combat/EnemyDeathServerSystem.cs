using Unity.Burst;
using Unity.Entities;
using Unity.NetCode;

[BurstCompile]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
public partial struct EnemyDeathServerSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);

        foreach (var (hp, e) in SystemAPI.Query<RefRO<EnemyHealthData>>().WithEntityAccess())
        {
            if (hp.ValueRO.Current <= 0)
                ecb.DestroyEntity(e);
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }
}
