using Unity.Entities;
using Unity.Collections;
using Unity.NetCode;
using Shared;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
public partial struct HandleUpgradeRequestSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GlobalUpgradeMultipliers>();
    }

    public void OnUpdate(ref SystemState state)
    {
        var ecb = new EntityCommandBuffer(Allocator.Temp);
        var multipliersRW = SystemAPI.GetSingletonRW<GlobalUpgradeMultipliers>();

        foreach (var (rpcReceive, rpcEntity) in SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>>()
            .WithAll<UpgradeRequestRpc>()
            .WithEntityAccess())
        {
            var rpc = state.EntityManager.GetComponentData<UpgradeRequestRpc>(rpcEntity);

            switch (rpc.unitType)
            {
                case UnitTypeEnum.Infantry:
                    if (rpc.statType == UpgradeStatType.MoveSpeed)
                        multipliersRW.ValueRW.infantryMoveSpeedMultiplier += rpc.multiplierChange;
                    else
                        multipliersRW.ValueRW.infantryAttackPowerMultiplier += rpc.multiplierChange;
                    break;
                case UnitTypeEnum.Tank:
                    if (rpc.statType == UpgradeStatType.MoveSpeed)
                        multipliersRW.ValueRW.tankMoveSpeedMultiplier += rpc.multiplierChange;
                    else
                        multipliersRW.ValueRW.tankAttackPowerMultiplier += rpc.multiplierChange;
                    break;
                case UnitTypeEnum.Scout:
                    if (rpc.statType == UpgradeStatType.MoveSpeed)
                        multipliersRW.ValueRW.scoutMoveSpeedMultiplier += rpc.multiplierChange;
                    else
                        multipliersRW.ValueRW.scoutAttackPowerMultiplier += rpc.multiplierChange;
                    break;
                case UnitTypeEnum.Artillery:
                    if (rpc.statType == UpgradeStatType.MoveSpeed)
                        multipliersRW.ValueRW.artilleryMoveSpeedMultiplier += rpc.multiplierChange;
                    else
                        multipliersRW.ValueRW.artilleryAttackPowerMultiplier += rpc.multiplierChange;
                    break;
            }

            ecb.DestroyEntity(rpcEntity);
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }
}
