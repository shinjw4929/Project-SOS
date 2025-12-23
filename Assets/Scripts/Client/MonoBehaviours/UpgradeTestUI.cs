using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using Shared;

public class UpgradeTestUI : MonoBehaviour
{
    private World _clientWorld;
    private EntityManager _clientEntityManager;

    private void OnGUI()
    {
        if (!TryInitClientWorld()) return;

        GUILayout.BeginArea(new Rect(10, 10, 300, 400));
        GUILayout.Label("=== 업그레이드 테스트 ===");

        if (GUILayout.Button("Infantry 이동속도 +10%"))
            SendUpgradeRpc(UnitTypeEnum.Infantry, UpgradeStatType.MoveSpeed, 0.1f);

        if (GUILayout.Button("Infantry 공격력 +20%"))
            SendUpgradeRpc(UnitTypeEnum.Infantry, UpgradeStatType.AttackPower, 0.2f);

        if (GUILayout.Button("Tank 이동속도 +15%"))
            SendUpgradeRpc(UnitTypeEnum.Tank, UpgradeStatType.MoveSpeed, 0.15f);

        if (GUILayout.Button("Tank 공격력 +25%"))
            SendUpgradeRpc(UnitTypeEnum.Tank, UpgradeStatType.AttackPower, 0.25f);

        GUILayout.EndArea();
    }

    private void SendUpgradeRpc(UnitTypeEnum type, UpgradeStatType stat, float change)
    {
        Entity rpcEntity = _clientEntityManager.CreateEntity();
        _clientEntityManager.AddComponentData(rpcEntity, new UpgradeRequestRpc
        {
            unitType = type,
            statType = stat,
            multiplierChange = change
        });
        _clientEntityManager.AddComponent<SendRpcCommandRequest>(rpcEntity);
    }

    private bool TryInitClientWorld()
    {
        if (_clientWorld != null && _clientWorld.IsCreated)
            return true;

        foreach (var world in World.All)
        {
            if (world.IsClient())
            {
                _clientWorld = world;
                _clientEntityManager = world.EntityManager;
                return true;
            }
        }

        return false;
    }
}
