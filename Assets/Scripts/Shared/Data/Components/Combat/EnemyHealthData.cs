using Unity.Entities;

public struct EnemyHealthData : IComponentData
{
    public int Max;
    public int Current;
}
