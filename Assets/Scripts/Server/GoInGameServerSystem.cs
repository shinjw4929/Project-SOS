using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
partial struct GoInGameServerSystem : ISystem
{
    //[BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<EntitiesReferences>();
        state.RequireForUpdate<NetworkId>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        EntityCommandBuffer entityCommandBuffer = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
        
        EntitiesReferences entitiesReferences = SystemAPI.GetSingleton<EntitiesReferences>();
        
        foreach ((
                     RefRO<ReceiveRpcCommandRequest> receiveRpcCommandRequest,
                     Entity entity)
                 in SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>>().WithAll<GoInGameRequestRpc>().WithEntityAccess())
        {
          entityCommandBuffer.AddComponent<NetworkStreamInGame>(receiveRpcCommandRequest.ValueRO.SourceConnection);

          Debug.Log("Client Connected to Server!");
          
          Entity playerEntity = entityCommandBuffer.Instantiate(entitiesReferences.playerPrefabEntity);
          entityCommandBuffer.SetComponent(playerEntity, LocalTransform.FromPosition(new float3(
              UnityEngine.Random.Range(-10, 10), 0, 0
              )));
          
          NetworkId networkId = SystemAPI.GetComponent<NetworkId>(receiveRpcCommandRequest.ValueRO.SourceConnection);
          entityCommandBuffer.AddComponent(playerEntity, new GhostOwner
          {
              NetworkId = networkId.Value,
          });
          
          entityCommandBuffer.AppendToBuffer(receiveRpcCommandRequest.ValueRO.SourceConnection, new LinkedEntityGroup
          {
              Value = playerEntity,
          });
          
          
          entityCommandBuffer.DestroyEntity(entity);
        }
        entityCommandBuffer.Playback(state.EntityManager);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        
    }
}
