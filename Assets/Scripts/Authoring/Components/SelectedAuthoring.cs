using Unity.Entities;
using UnityEngine;

namespace Authoring
{
    // Selected 컴포넌트를 Player 프리팹에 추가하기 위한 Authoring
    public class SelectedAuthoring : MonoBehaviour
    {
        public class Baker : Baker<SelectedAuthoring>
        {
            public override void Bake(SelectedAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);

                // Selected 컴포넌트 추가
                AddComponent<Shared.Selected>(entity);

                // 초기에는 비활성화 (선택되지 않음)
                SetComponentEnabled<Shared.Selected>(entity, false);
            }
        }
    }
}
