using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;

[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial struct FireProjectileClientSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        // 연결이 "게임 상태"에 들어간 뒤에만 동작
        state.RequireForUpdate<NetworkStreamInGame>();
    }

    public void OnUpdate(ref SystemState state)
    {
        if (!Input.GetKeyDown(KeyCode.F))
            return;

        var cam = Camera.main;
        if (cam == null)
            return;

        // 마우스 -> 월드(XZ 평면 y=0) 교차점
        var ray = cam.ScreenPointToRay(Input.mousePosition);
        if (Mathf.Abs(ray.direction.y) < 0.00001f)
            return;

        float planeY = 0f;
        float t = (planeY - ray.origin.y) / ray.direction.y;
        if (t <= 0f)
            return;

        float3 targetPos = (float3)(ray.origin + ray.direction * t);

        // ✅ NetworkStreamConnection을 안 씀 (Transport 참조 문제 회피)
        // 연결 엔티티는 클라이언트에서 보통 1개라 첫 번째만 잡으면 됨
        Entity targetConn = Entity.Null;
        foreach (var (_, connEntity) in SystemAPI.Query<RefRO<NetworkStreamInGame>>().WithEntityAccess())
        {
            targetConn = connEntity;
            break;
        }

        if (targetConn == Entity.Null)
            return;

        var em = state.EntityManager;

        var rpcEntity = em.CreateEntity();
        em.AddComponentData(rpcEntity, new FireProjectileRpc
        {
            TargetPosition = targetPos
        });
        em.AddComponentData(rpcEntity, new SendRpcCommandRequest
        {
            TargetConnection = targetConn
        });
    }
}
