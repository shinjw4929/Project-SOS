using Unity.Entities;
using Unity.NetCode;

namespace Shared
{
    [GhostComponent]
    public struct GlobalUpgradeMultipliers : IComponentData
    {
        [GhostField] public float infantryMoveSpeedMultiplier;
        [GhostField] public float infantryAttackPowerMultiplier;
        [GhostField] public float tankMoveSpeedMultiplier;
        [GhostField] public float tankAttackPowerMultiplier;
        [GhostField] public float scoutMoveSpeedMultiplier;
        [GhostField] public float scoutAttackPowerMultiplier;
        [GhostField] public float artilleryMoveSpeedMultiplier;
        [GhostField] public float artilleryAttackPowerMultiplier;
    }
}
