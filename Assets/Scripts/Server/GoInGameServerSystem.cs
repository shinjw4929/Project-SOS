using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using Shared; // [필수] Player 컴포넌트(TeamId)를 수정하기 위해 필요

[BurstCompile]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
partial struct GoInGameServerSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<UnitCatalog>();
        state.RequireForUpdate<NetworkId>();
        state.RequireForUpdate<UserResourcesPrefabRef>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        EntityCommandBuffer entityCommandBuffer = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
        
        var unitCatalog = SystemAPI.GetSingletonEntity<UnitCatalog>();
        var unitBuffer = SystemAPI.GetBuffer<UnitCatalogElement>(unitCatalog);
        var playerResourcesPrefab = SystemAPI.GetSingleton<UserResourcesPrefabRef>().Prefab;
        
        foreach ((
                     RefRO<ReceiveRpcCommandRequest> receiveRpcCommandRequest,
                     Entity entity)
                 in SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>>().WithAll<GoInGameRequestRpc>().WithEntityAccess())
        {
          entityCommandBuffer.AddComponent<NetworkStreamInGame>(receiveRpcCommandRequest.ValueRO.SourceConnection);

          // 서버 연결 디버깅 시 사용
          // Debug.Log("Client Connected to Server!");
          
          // 1. 영웅 유닛 생성
          Entity heroEntity = entityCommandBuffer.Instantiate(unitBuffer[0].PrefabEntity);
          
          // 프리팹 엔티티의 원본 LocalTransform을 읽어옵니다.
          LocalTransform prefabTransform = SystemAPI.GetComponent<LocalTransform>(unitBuffer[0].PrefabEntity);
          float prefabY = prefabTransform.Position.y;
          
          // 2. 랜덤 위치 배치
          entityCommandBuffer.SetComponent(heroEntity, LocalTransform.FromPosition(new float3(
              UnityEngine.Random.Range(-7, 7), prefabY, 7)
          ));
          
          // 3. 접속한 클라이언트의 고유 ID (0, 1, 2...) 가져오기
          NetworkId networkId = SystemAPI.GetComponent<NetworkId>(receiveRpcCommandRequest.ValueRO.SourceConnection);
          
          // 4. 네트워크 소유권 설정 (Netcode 필수)
          entityCommandBuffer.AddComponent(heroEntity, new GhostOwner
          {
              NetworkId = networkId.Value,
          });
          
          // 5. Hero 팀 번호(TeamId) 부여하기
          entityCommandBuffer.SetComponent(heroEntity, new Team
          {
              teamId = networkId.Value // 접속 ID를 그대로 팀 번호로 사용
          });
          
          entityCommandBuffer.AppendToBuffer(receiveRpcCommandRequest.ValueRO.SourceConnection, new LinkedEntityGroup
          {
              Value = heroEntity,
          });

          // 6. 플레이어 자원 엔티티 생성 (Ghost 프리팹 인스턴스화)
          Entity resourceEntity = entityCommandBuffer.Instantiate(playerResourcesPrefab);
          entityCommandBuffer.AddComponent(resourceEntity, new GhostOwner
          {
              NetworkId = networkId.Value,
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