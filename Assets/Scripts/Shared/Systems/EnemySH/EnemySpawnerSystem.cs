using Unity.Burst;
using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;
using Unity.Mathematics;
using Shared;


[BurstCompile]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
public partial struct EnemySpawnerSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<EnemyGhostPrefab>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // 한 번만 실행
        state.Enabled = false;

        var prefabEntity =
            SystemAPI.GetSingleton<EnemyGhostPrefab>().Prefab;

        Entity enemy = state.EntityManager.Instantiate(prefabEntity);

        state.EntityManager.SetComponentData(enemy, new LocalTransform
        {
            Position = new float3(0, 0.5f, 0),
            Rotation = quaternion.identity,
            Scale = 1f
        });
    }
}
