using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Shared;

namespace Server
{
    /// <summary>
    /// 클라이언트 카메라 위치 + 뷰포트 반크기(HalfExtent)를 Connection에 반영.
    /// GhostDistanceImportance(우선순위) + GhostRelevancySystem(전송 여부) 모두 이 위치를 기준으로 계산.
    /// 카메라 데이터는 CameraPositionRpc로 클라이언트에서 ~20Hz 주기로 수신.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial struct UpdateConnectionPositionSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var connectionPositionLookup = SystemAPI.GetComponentLookup<GhostConnectionPosition>();
            var viewExtentLookup = SystemAPI.GetComponentLookup<ConnectionViewExtent>();

            var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged);

            foreach (var (rpcReceive, rpc, rpcEntity) in
                     SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>, RefRO<CameraPositionRpc>>()
                         .WithEntityAccess())
            {
                var sourceConnection = rpcReceive.ValueRO.SourceConnection;
                if (connectionPositionLookup.HasComponent(sourceConnection))
                {
                    connectionPositionLookup[sourceConnection] = new GhostConnectionPosition
                    {
                        Position = rpc.ValueRO.Position
                    };
                }

                if (viewExtentLookup.HasComponent(sourceConnection))
                {
                    viewExtentLookup[sourceConnection] = new ConnectionViewExtent
                    {
                        HalfExtent = rpc.ValueRO.ViewHalfExtent
                    };
                }

                ecb.DestroyEntity(rpcEntity);
            }
        }
    }
}
