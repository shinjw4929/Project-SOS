using Unity.Entities;
using UnityEngine;

// Hero 프리팹에서 최대 HP / 시작 HP를 개발자가 조절할 수 있게 한다.
// HeroTag는 UnitAuthoring 쪽에서 이미 추가되므로 여기서는 추가하지 않는다.
public class HeroHealthAuthoring : MonoBehaviour
{
    public int maxHp = 100;
    public int startHp = 100;

    private class HeroHealthBaker : Unity.Entities.Baker<HeroHealthAuthoring>
    {
        public override void Bake(HeroHealthAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            int max = Mathf.Max(1, authoring.maxHp);
            int cur = Mathf.Clamp(authoring.startHp, 0, max);

            AddComponent(entity, new HeroHealth
            {
                Max = max,
                Current = cur
            });

            AddComponent(entity, new HeroDamageCooldown
            {
                TimeLeft = 0f
            });
        }
    }
}
