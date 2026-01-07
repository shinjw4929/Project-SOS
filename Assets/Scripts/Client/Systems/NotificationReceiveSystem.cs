using Unity.Entities;
using Unity.NetCode;
using Unity.Burst;
using Shared;

namespace Client
{
    /// <summary>
    /// 서버로부터 알림 RPC를 수신하여 NotificationState 싱글톤에 저장
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [BurstCompile]
    public partial struct NotificationReceiveSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkStreamInGame>();

            // NotificationState 싱글톤 생성
            var entity = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponentData(entity, new NotificationState
            {
                PendingNotification = NotificationType.None
            });
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged);

            foreach (var (rpc, rpcEntity) in
                SystemAPI.Query<RefRO<NotificationRpc>>()
                    .WithAll<ReceiveRpcCommandRequest>()
                    .WithEntityAccess())
            {
                // NotificationState 싱글톤 업데이트
                if (SystemAPI.TryGetSingletonRW<NotificationState>(out var notificationState))
                {
                    notificationState.ValueRW.PendingNotification = rpc.ValueRO.Type;
                }

                ecb.DestroyEntity(rpcEntity);
            }
        }
    }
}