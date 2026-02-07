using Unity.Entities;
using Unity.NetCode;
using Shared;

namespace Server
{
    /// <summary>
    /// GamePhaseState 싱글톤 초기화 시스템.
    /// 서버에서 1회 실행하여 게임 상태 엔티티 생성.
    /// </summary>
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial struct GamePhaseInitSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            // 1회 실행 후 비활성화
            state.Enabled = false;

            // GamePhaseState가 이미 존재하면 스킵
            if (SystemAPI.HasSingleton<GamePhaseState>()) return;

            var entity = state.EntityManager.CreateEntity(typeof(GamePhaseState));
            SystemAPI.SetComponent(entity, new GamePhaseState
            {
                CurrentWave = WavePhase.Wave0,
                ElapsedTime = 0f,
                TotalKillCount = 0,
                Wave0SpawnedCount = 0,
                LastSpawnTime = 0f
            });

#if UNITY_EDITOR
            state.EntityManager.SetName(entity, "Singleton_GamePhaseState");
#endif
        }
    }
}
