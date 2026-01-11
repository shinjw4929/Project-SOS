using Unity.Entities;
using Unity.NetCode;

// Hero인지 구분하기 위한 태그
public struct HeroTag : IComponentData
{
}

// 반드시 Ghost로 전송되게 설정한다.
[GhostComponent(PrefabType = GhostPrefabType.All)]
public struct HeroHealth : IComponentData
{
    [GhostField] public int Current;
    [GhostField] public int Max;
}

// 서버에서만 쓰는 쿨다운(전송 필요 없음)
public struct HeroDamageCooldown : IComponentData
{
    public float TimeLeft;
}
