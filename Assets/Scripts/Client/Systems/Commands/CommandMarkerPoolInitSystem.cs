using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Shared;

namespace Client
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct CommandMarkerPoolInitSystem : ISystem
    {
        private const int PoolSizePerType = 4;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<CommandMarkerPrefabRef>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.Enabled = false;

            var prefabRef = SystemAPI.GetSingleton<CommandMarkerPrefabRef>();
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            SpawnPoolForType(ref ecb, prefabRef.MoveMarkerPrefab, 1);
            SpawnPoolForType(ref ecb, prefabRef.GatherMarkerPrefab, 2);
            SpawnPoolForType(ref ecb, prefabRef.AttackMarkerPrefab, 3);

            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }

        private void SpawnPoolForType(ref EntityCommandBuffer ecb, Entity prefab, byte markerType)
        {
            if (prefab == Entity.Null) return;

            for (int i = 0; i < PoolSizePerType; i++)
            {
                Entity marker = ecb.Instantiate(prefab);
                ecb.SetComponent(marker, new LocalTransform
                {
                    Position = float3.zero,
                    Rotation = quaternion.identity,
                    Scale = 0f
                });
                ecb.SetComponent(marker, new CommandMarkerLifetime
                {
                    TotalTime = 1.0f,
                    RemainingTime = 0f,
                    InitialScale = 2.0f,
                    MarkerType = markerType
                });
            }
        }
    }
}
