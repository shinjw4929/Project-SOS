using Unity.Entities;
using UnityEngine;

namespace Authoring
{
    /// <summary>
    /// Selection Ring 프리팹 Baker
    /// 색상은 Material에서 직접 설정 (팀별 프리팹 분리)
    /// </summary>
    public class SelectionRingAuthoring : MonoBehaviour
    {
        public class Baker : Baker<SelectionRingAuthoring>
        {
            public override void Bake(SelectionRingAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);

                // Ring 식별 태그
                AddComponent<Shared.SelectionRingTag>(entity);

                // 소유자 참조 (런타임에 설정)
                AddComponent(entity, new Shared.SelectionRingOwner
                {
                    OwnerEntity = Entity.Null
                });
            }
        }
    }
}
