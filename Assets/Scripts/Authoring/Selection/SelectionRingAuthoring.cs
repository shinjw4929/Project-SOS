using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;

namespace Authoring
{
    /// <summary>
    /// Selection Ring 프리팹 Baker
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

                // 색상 제어용 (BaseColor 사용 - URP per-instance 지원)
                AddComponent(entity, new URPMaterialPropertyBaseColor
                {
                    Value = new float4(0f, 1f, 0f, 1f)
                });
            }
        }
    }
}
