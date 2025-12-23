using Unity.Entities;
using Shared;

[UpdateInGroup(typeof(InitializationSystemGroup))]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
public partial struct InitializeUpgradeSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        Entity singletonEntity = state.EntityManager.CreateEntity();
        state.EntityManager.AddComponentData(singletonEntity, new GlobalUpgradeMultipliers
        {
            infantryMoveSpeedMultiplier = 1.0f,
            infantryAttackPowerMultiplier = 1.0f,
            tankMoveSpeedMultiplier = 1.0f,
            tankAttackPowerMultiplier = 1.0f,
            scoutMoveSpeedMultiplier = 1.0f,
            scoutAttackPowerMultiplier = 1.0f,
            artilleryMoveSpeedMultiplier = 1.0f,
            artilleryAttackPowerMultiplier = 1.0f,
        });

        state.Enabled = false;
    }
}
