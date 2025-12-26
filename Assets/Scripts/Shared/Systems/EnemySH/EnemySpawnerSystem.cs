using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;
using Unity.Mathematics;
using Shared;


[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
public partial struct EnemySpawnerSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<EnemyGhostPrefab>();
    }

    public void OnUpdate(ref SystemState state)
    {
        // �� ���� ����
        state.Enabled = false;

        var prefabEntity =
            SystemAPI.GetSingleton<EnemyGhostPrefab>().Prefab;

        Entity enemy = state.EntityManager.Instantiate(prefabEntity);

        state.EntityManager.SetComponentData(enemy, new LocalTransform
        {
            Position = new float3(0, 0, 0),
            Rotation = quaternion.identity,
            Scale = 1f
        });
    }
}
