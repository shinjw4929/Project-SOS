using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
public partial class RpcDebugServerSystem : SystemBase
{
    protected override void OnUpdate()
    {
        int count = 0;
        foreach (var _ in SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>, RefRO<FireProjectileRpc>>())
            count++;

        if (count > 0)
            Debug.Log($"[Server] FireProjectileRpc received: {count}");
    }
}
