using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;
using Shared;

namespace Server
{
    /// <summary>
    /// Hero 위치를 Connection의 GhostConnectionPosition에 반영.
    /// GhostDistanceImportance가 이 위치를 기준으로 Ghost 우선순위를 계산.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial struct UpdateConnectionPositionSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var transformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true);

            new UpdateJob
            {
                TransformLookup = transformLookup
            }.ScheduleParallel();
        }

        [BurstCompile]
        partial struct UpdateJob : IJobEntity
        {
            [ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;

            public void Execute(ref GhostConnectionPosition conPos, in UserAliveState aliveState)
            {
                if (!aliveState.IsAlive) return;
                if (!TransformLookup.HasComponent(aliveState.HeroEntity)) return;
                conPos.Position = TransformLookup[aliveState.HeroEntity].Position;
            }
        }
    }
}
