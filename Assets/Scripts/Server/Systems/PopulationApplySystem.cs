using Unity.Burst;
using Unity.Entities;
using Shared;

namespace Server
{
    /// <summary>
    /// PopulationEvent 버퍼를 소비하여 UserSupply에 적용.
    /// 주로 유닛 사망 시 인구수 감소 처리.
    /// </summary>
    [UpdateInGroup(typeof(LateSimulationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [BurstCompile]
    public partial struct PopulationApplySystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<UserEconomyTag>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            new PopulationApplyJob().Schedule();
        }
    }

    [BurstCompile]
    [WithAll(typeof(UserEconomyTag))]
    public partial struct PopulationApplyJob : IJobEntity
    {
        private void Execute(ref UserSupply supply, ref DynamicBuffer<PopulationEvent> events)
        {
            if (events.Length == 0) return;

            int totalDelta = 0;
            for (int i = 0; i < events.Length; i++)
            {
                totalDelta += events[i].Delta;
            }

            supply.Currentvalue += totalDelta;

            // 0 미만 방지
            if (supply.Currentvalue < 0)
                supply.Currentvalue = 0;

            events.Clear();
        }
    }
}
