using Unity.Entities;

public struct Faction : IComponentData
{
    public byte Value; // 1=Player, 2=Enemy 같은 식으로 사용
}
