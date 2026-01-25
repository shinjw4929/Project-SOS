using Unity.Burst;
using Unity.Entities;
using Unity.Collections;
using Unity.NetCode;
using Shared;

/// <summary>
/// Hero 사망 감지 및 게임오버 처리 시스템 (서버 전용)
/// ServerDeathSystem 이전에 실행되어 Hero 파괴 전에 상태 업데이트
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(ServerDeathSystem))]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[BurstCompile]
public partial struct HeroDeathDetectionSystem : ISystem
{
    private bool _gameOverSent;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<NetworkId>();
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        _gameOverSent = false;
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (_gameOverSent) return;

        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        // NetworkId -> Connection 엔티티 매핑 생성
        var networkIdToConnection = new NativeParallelHashMap<int, Entity>(16, Allocator.Temp);
        foreach (var (networkId, entity) in SystemAPI.Query<RefRO<NetworkId>>()
                     .WithAll<NetworkStreamInGame>()
                     .WithEntityAccess())
        {
            networkIdToConnection.TryAdd(networkId.ValueRO.Value, entity);
        }

        // Hero 사망 감지 (Health <= 0 + HeroTag)
        bool anyHeroDied = false;
        foreach (var (health, ghostOwner, entity) in SystemAPI.Query<RefRO<Health>, RefRO<GhostOwner>>()
                     .WithAll<HeroTag>()
                     .WithEntityAccess())
        {
            if (health.ValueRO.CurrentValue <= 0)
            {
                int networkId = ghostOwner.ValueRO.NetworkId;

                // Connection 엔티티의 UserAliveState 업데이트
                if (networkIdToConnection.TryGetValue(networkId, out Entity connectionEntity))
                {
                    if (SystemAPI.HasComponent<UserAliveState>(connectionEntity))
                    {
                        var aliveState = SystemAPI.GetComponentRW<UserAliveState>(connectionEntity);
                        if (aliveState.ValueRO.IsAlive)
                        {
                            aliveState.ValueRW.IsAlive = false;
                            anyHeroDied = true;

                            // HeroDeathRpc 전송 (해당 유저에게)
                            var rpcEntity = ecb.CreateEntity();
                            ecb.AddComponent<HeroDeathRpc>(rpcEntity);
                            ecb.AddComponent(rpcEntity, new SendRpcCommandRequest
                            {
                                TargetConnection = connectionEntity
                            });
                        }
                    }
                }
            }
        }

        networkIdToConnection.Dispose();

        // 게임오버 체크 (Hero 사망이 발생했을 때만)
        if (anyHeroDied)
        {
            bool allDead = true;
            int connectionCount = 0;

            foreach (var aliveState in SystemAPI.Query<RefRO<UserAliveState>>())
            {
                connectionCount++;
                if (aliveState.ValueRO.IsAlive)
                {
                    allDead = false;
                    break;
                }
            }

            // 모든 유저가 사망했고 최소 1명 이상 있었을 때
            if (allDead && connectionCount > 0)
            {
                _gameOverSent = true;

                // GameOverRpc 브로드캐스트 (모든 클라이언트에게)
                var gameOverRpcEntity = ecb.CreateEntity();
                ecb.AddComponent<GameOverRpc>(gameOverRpcEntity);
                ecb.AddComponent<SendRpcCommandRequest>(gameOverRpcEntity);
            }
        }
    }
}
