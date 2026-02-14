using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;
using Shared;

namespace Client
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct CommandMarkerFadeSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<CommandMarkerTag>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float deltaTime = SystemAPI.Time.DeltaTime;

            foreach (var (lifetime, transform) in
                SystemAPI.Query<RefRW<CommandMarkerLifetime>, RefRW<LocalTransform>>()
                    .WithAll<CommandMarkerTag>())
            {
                if (lifetime.ValueRO.RemainingTime <= 0f)
                    continue;

                lifetime.ValueRW.RemainingTime -= deltaTime;

                if (lifetime.ValueRO.RemainingTime <= 0f)
                {
                    lifetime.ValueRW.RemainingTime = 0f;
                    transform.ValueRW.Scale = 0f;
                    continue;
                }

                float ratio = lifetime.ValueRO.RemainingTime / lifetime.ValueRO.TotalTime;
                transform.ValueRW.Scale = lifetime.ValueRO.InitialScale * ratio;
            }
        }
    }
}
