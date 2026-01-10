using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;

/// <summary>
/// 투사체 이동 및 소멸 시스템 (서버)
/// </summary>
[BurstCompile]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
public partial struct ProjectileMoveServerSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float dt = SystemAPI.Time.DeltaTime;

        var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                           .CreateCommandBuffer(state.WorldUnmanaged);

        foreach (var (transform, move, entity) in
                 SystemAPI.Query<RefRW<LocalTransform>, RefRW<ProjectileMove>>()
                          .WithNone<Prefab>()
                          .WithEntityAccess())
        {
            ref readonly var moveData = ref move.ValueRO;
            float step = moveData.Speed * dt;

            if (step > moveData.RemainingDistance)
                step = moveData.RemainingDistance;

            transform.ValueRW.Position += moveData.Direction * step;
            move.ValueRW.RemainingDistance -= step;

            if (move.ValueRO.RemainingDistance <= 0f)
                ecb.DestroyEntity(entity);
        }
    }
}
