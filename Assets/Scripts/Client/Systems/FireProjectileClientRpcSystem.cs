using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
[UpdateInGroup(typeof(GhostInputSystemGroup))]
public partial class FireProjectileClientRpcSystem : SystemBase
{
    protected override void OnCreate()
    {
        RequireForUpdate<NetworkId>();
    }

    protected override void OnUpdate()
    {
        // 예: 마우스 좌클릭 / 스페이스로 발사
        if (!Input.GetMouseButtonDown(0) && !Input.GetKeyDown(KeyCode.Space))
            return;

        // 클라이언트 연결(커넥션) 엔티티 찾기
        Entity connection = Entity.Null;
        foreach (var (id, e) in SystemAPI.Query<RefRO<NetworkId>>().WithEntityAccess())
        {
            connection = e;
            break;
        }

        if (connection == Entity.Null)
            return;

        // RPC 엔티티 생성 + 전송 요청
        var rpcEntity = EntityManager.CreateEntity();
        EntityManager.AddComponentData(rpcEntity, default(FireProjectileRpc));
        EntityManager.AddComponentData(rpcEntity, new SendRpcCommandRequest
        {
            TargetConnection = connection
        });
    }
}
