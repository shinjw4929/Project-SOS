using Shared;
using Unity.Entities;
using UnityEngine;

/// <summary>
/// Worker가 운반하는 자원 시각화 프리팹 Authoring
/// 프리팹에 이 컴포넌트를 추가하면 필요한 컴포넌트들이 베이킹됨
/// </summary>
public class CarriedResourceAuthoring : MonoBehaviour
{
    class Baker : Baker<CarriedResourceAuthoring>
    {
        public override void Bake(CarriedResourceAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<CarriedResourceTag>(entity);
            // WorkerEntity는 런타임에 SetComponent로 설정
            AddComponent<CarriedResourceOwner>(entity);
        }
    }
}
