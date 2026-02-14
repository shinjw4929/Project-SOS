using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Shared;

namespace Server
{
    /// <summary>
    /// GamePhaseState, ClientServerTickRate, GhostDistanceImportance 싱글톤 초기화.
    /// 서버에서 1회 실행.
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

            // ClientServerTickRate: 시뮬레이션 60Hz, 네트워크 전송 30Hz
            // 스냅샷 전송 빈도를 절반으로 낮춰 대역폭 50% 절감
            var tickRate = new ClientServerTickRate
            {
                SimulationTickRate = 60,
                NetworkTickRate = 30,
            };
            tickRate.ResolveDefaults();
            state.EntityManager.CreateSingleton(tickRate);

            // GhostDistanceImportance: 카메라에 가까운 Ghost 우선 업데이트
            // TileSize 10: AABB Relevancy로 Ghost 수가 ~400 제한되므로 관대한 gradient 사용
            var gridSingleton = state.EntityManager.CreateSingleton(new GhostDistanceData
            {
                TileSize = new int3(10, 10, 10),
                TileCenter = int3.zero,
                TileBorderWidth = new float3(1f, 1f, 1f),
            });
            state.EntityManager.AddComponentData(gridSingleton, new GhostImportance
            {
                BatchScaleImportanceFunction = GhostDistanceImportance.BatchScaleFunctionPointer,
                GhostConnectionComponentType = ComponentType.ReadOnly<GhostConnectionPosition>(),
                GhostImportanceDataType = ComponentType.ReadOnly<GhostDistanceData>(),
                GhostImportancePerChunkDataType = ComponentType.ReadOnly<GhostDistancePartitionShared>(),
            });
#if UNITY_EDITOR
            state.EntityManager.SetName(gridSingleton, "Singleton_GhostDistanceImportance");
#endif

            // DefaultSnapshotPacketSize: 기본 MTU(~1400) 사용
            // Ghost Relevancy(AABB)로 relevant Ghost를 200~400으로 제한하므로 MTU 패킷으로 충분
            // 4096 사용 시 UDP fragment 유실 → 전체 스냅샷 소실 위험
        }
    }
}
