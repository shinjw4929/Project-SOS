using Unity.Burst;
using Unity.Entities;
using Unity.NetCode;
using Shared;

namespace Server
{
    /// <summary>
    /// Wave 전환 조건 체크 및 GamePhaseState 업데이트.
    /// 시간 OR 처치 수 조건 충족 시 다음 Wave로 전환.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial struct WaveManagerSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GamePhaseState>();
            state.RequireForUpdate<GameSettings>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var gameSettings = SystemAPI.GetSingleton<GameSettings>();
            float deltaTime = SystemAPI.Time.DeltaTime;

            foreach (var phaseState in SystemAPI.Query<RefRW<GamePhaseState>>())
            {
                // 경과 시간 업데이트
                phaseState.ValueRW.ElapsedTime += deltaTime;

                float elapsed = phaseState.ValueRO.ElapsedTime;
                int kills = phaseState.ValueRO.TotalKillCount;
                WavePhase currentWave = phaseState.ValueRO.CurrentWave;

                // Wave 전환 조건 체크 (OR 조건)
                switch (currentWave)
                {
                    case WavePhase.Wave0:
                        if (elapsed >= gameSettings.Wave1TriggerTime ||
                            kills >= gameSettings.Wave1TriggerKillCount)
                        {
                            phaseState.ValueRW.CurrentWave = WavePhase.Wave1;
                            // Wave1 시작 시 스폰 타이머 리셋
                            phaseState.ValueRW.SpawnTimer = 0f;
                        }
                        break;

                    case WavePhase.Wave1:
                        if (elapsed >= gameSettings.Wave2TriggerTime ||
                            kills >= gameSettings.Wave2TriggerKillCount)
                        {
                            phaseState.ValueRW.CurrentWave = WavePhase.Wave2;
                            // Wave2 시작 시 스폰 타이머 리셋
                            phaseState.ValueRW.SpawnTimer = 0f;
                        }
                        break;

                    case WavePhase.Wave2:
                        // 추후 Wave3+ 또는 GameOver 조건 추가 가능
                        break;
                }
            }
        }
    }
}
