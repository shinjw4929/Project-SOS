using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using Shared;

namespace Client
{
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    partial struct GoInGameClientSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkId>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // RoomAuthState에서 토큰 확인 -- 토큰 없으면 GameStart 수신 전이므로 대기
            if (!SystemAPI.TryGetSingleton<RoomAuthState>(out var authState) || authState.AuthToken.Length == 0)
                return;

            EntityCommandBuffer entityCommandBuffer = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
            foreach ((
                         RefRO<NetworkId> networkId,
                         Entity entity)
                     in SystemAPI.Query<RefRO<NetworkId>>().WithNone<NetworkStreamInGame>().WithEntityAccess())
            {
                entityCommandBuffer.AddComponent<NetworkStreamInGame>(entity);

                GameLogger.Info(LogWorld.Client, LogCategory.Network,
                    (FixedString128Bytes)"Connected to server with auth token");

                Entity rpcEntity = entityCommandBuffer.CreateEntity();
                entityCommandBuffer.AddComponent(rpcEntity, new GoInGameRequestRpc
                {
                    AuthToken = authState.AuthToken
                });
                entityCommandBuffer.AddComponent(rpcEntity, new SendRpcCommandRequest());
            }
            entityCommandBuffer.Playback(state.EntityManager);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {

        }
    }
}