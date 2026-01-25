using Unity.Entities;
using Unity.NetCode;
using Shared;

namespace Client
{
    /// <summary>
    /// 게임오버 관련 RPC 수신 처리 시스템 (클라이언트 전용)
    /// RPC 수신 시 이벤트 발생하여 UI에 알림
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial class GameOverReceiveSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<NetworkStreamInGame>();
        }

        protected override void OnUpdate()
        {
            var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(World.Unmanaged);

            // HeroDeathRpc 수신 처리
            foreach (var (_, rpcEntity) in SystemAPI.Query<RefRO<HeroDeathRpc>>()
                         .WithAll<ReceiveRpcCommandRequest>()
                         .WithEntityAccess())
            {
                // UserState를 Dead로 변경
                if (SystemAPI.TryGetSingletonRW<UserState>(out var userState))
                {
                    userState.ValueRW.CurrentState = UserContext.Dead;
                }

                // 이벤트 발생
                GameOverEvents.RaiseHeroDeath();

                ecb.DestroyEntity(rpcEntity);
            }

            // GameOverRpc 수신 처리
            foreach (var (_, rpcEntity) in SystemAPI.Query<RefRO<GameOverRpc>>()
                         .WithAll<ReceiveRpcCommandRequest>()
                         .WithEntityAccess())
            {
                // 이벤트 발생
                GameOverEvents.RaiseGameOver();

                ecb.DestroyEntity(rpcEntity);
            }
        }
    }
}
