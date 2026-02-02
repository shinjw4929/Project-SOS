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
        state.RequireForUpdate<UserEconomyPrefabRef>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        EntityCommandBuffer entityCommandBuffer = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
        
        var unitCatalog = SystemAPI.GetSingletonEntity<UnitCatalog>();
        var unitBuffer = SystemAPI.GetBuffer<UnitCatalogElement>(unitCatalog);
        var userEconomyPrefab = SystemAPI.GetSingleton<UserEconomyPrefabRef>().Prefab;
        
        foreach ((
                     RefRO<ReceiveRpcCommandRequest> receiveRpcCommandRequest,
                     Entity entity)
                 in SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>>().WithAll<GoInGameRequestRpc>().WithEntityAccess())
        {
          entityCommandBuffer.AddComponent<NetworkStreamInGame>(receiveRpcCommandRequest.ValueRO.SourceConnection);

          // 서버 연결 디버깅 시 사용
          // Debug.Log("Client Connected to Server!");
          
          // // 1. 영웅 유닛 생성
          // Entity heroEntity = entityCommandBuffer.Instantiate(unitBuffer[0].PrefabEntity);
          //
          // // 프리팹 엔티티의 원본 LocalTransform을 읽어옵니다.
          // LocalTransform prefabTransform = SystemAPI.GetComponent<LocalTransform>(unitBuffer[0].PrefabEntity);
          float prefabY = 0f;
          //
          // // 2. 랜덤 위치 배치
          // entityCommandBuffer.SetComponent(heroEntity, LocalTransform.FromPosition(new float3(
          //     UnityEngine.Random.Range(-7, 7), prefabY, 10)
          // ));
          
          // 3. 접속한 클라이언트의 고유 ID (0, 1, 2...) 가져오기
          NetworkId networkId = SystemAPI.GetComponent<NetworkId>(receiveRpcCommandRequest.ValueRO.SourceConnection);

          // // 4. 네트워크 소유권 설정 (Netcode 필수)
          // entityCommandBuffer.AddComponent(heroEntity, new GhostOwner
          // {
          //     NetworkId = networkId.Value,
          // });
          //
          // // 5. Hero 팀 번호(TeamId) 부여하기
          // entityCommandBuffer.SetComponent(heroEntity, new Team
          // {
          //     teamId = networkId.Value // 접속 ID를 그대로 팀 번호로 사용
          // });
          //
          // entityCommandBuffer.AppendToBuffer(receiveRpcCommandRequest.ValueRO.SourceConnection, new LinkedEntityGroup
          // {
          //     Value = heroEntity,
          // });

          // ===== [테스트] 플레이어1 히어로 3200마리 대량 스폰 =====
          if (networkId.Value == 1)
          {
              const int testSpawnCount = 1600;
              const float gridSpacing = 1.0f;
              int gridSize = (int)math.ceil(math.sqrt(testSpawnCount)); // 57

              Entity workerPrefab = unitBuffer[2].PrefabEntity;
              float3 spawnCenter = new float3(0f, prefabY, 10f);
              float gridHalf = gridSize / 2f;

              for (int i = 0; i < testSpawnCount; i++)
              {
                  int gx = i % gridSize;
                  int gz = i / gridSize;

                  float3 pos = new float3(
                      (gx - gridHalf) * gridSpacing + spawnCenter.x,
                      spawnCenter.y,
                      (gz - gridHalf) * gridSpacing + spawnCenter.z
                  );

                  Entity testHero = entityCommandBuffer.Instantiate(workerPrefab);
                  entityCommandBuffer.SetComponent(testHero, LocalTransform.FromPosition(pos));
                  entityCommandBuffer.AddComponent(testHero, new GhostOwner { NetworkId = networkId.Value });
                  entityCommandBuffer.SetComponent(testHero, new Team { teamId = networkId.Value });
                  entityCommandBuffer.AppendToBuffer(receiveRpcCommandRequest.ValueRO.SourceConnection, new LinkedEntityGroup { Value = testHero });
              }
          }
          // ===== [테스트 끝] =====

          // // 6. 유저 생존 상태 추가 (Connection 엔티티에 부착)
          // entityCommandBuffer.AddComponent(receiveRpcCommandRequest.ValueRO.SourceConnection, new UserAliveState
          // {
          //     IsAlive = true,
          //     HeroEntity = heroEntity
          // });

          // 7. 플레이어 자원 엔티티 생성 (Ghost 프리팹 인스턴스화)
          Entity economyEntity = entityCommandBuffer.Instantiate(userEconomyPrefab);
          entityCommandBuffer.AddComponent(economyEntity, new GhostOwner
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