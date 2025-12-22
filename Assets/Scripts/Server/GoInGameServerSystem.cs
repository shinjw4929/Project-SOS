using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using Shared; // [필수] Player 컴포넌트(TeamId)를 수정하기 위해 필요

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
          
          // 1. 유닛 생성
          Entity playerEntity = entityCommandBuffer.Instantiate(entitiesReferences.playerPrefabEntity);
          
          // 2. 랜덤 위치 배치
          entityCommandBuffer.SetComponent(playerEntity, LocalTransform.FromPosition(new float3(
              UnityEngine.Random.Range(-10, 10), 0, 0
              )));
          
          // 3. 접속한 클라이언트의 고유 ID (0, 1, 2...) 가져오기
          NetworkId networkId = SystemAPI.GetComponent<NetworkId>(receiveRpcCommandRequest.ValueRO.SourceConnection);
          
          // 4. 네트워크 소유권 설정 (Netcode 필수)
          entityCommandBuffer.AddComponent(playerEntity, new GhostOwner
          {
              NetworkId = networkId.Value,
          });

          // ================================================================
          // [핵심 추가 사항] Player 데이터에 팀 번호(TeamId) 부여하기
          // ================================================================
          // 이 코드가 있어야 UnitSelectionSystem에서 "내 팀인가?"를 확인할 수 있음
          entityCommandBuffer.SetComponent(playerEntity, new Player
          {
              TeamId = networkId.Value // 접속 ID를 그대로 팀 번호로 사용
          });
          // ================================================================
          
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