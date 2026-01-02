using Unity.Entities;
using Unity.NetCode;

public struct EnemyHealthData : IComponentData
{
    [GhostField] public int Max;
    [GhostField] public int Current;
}
