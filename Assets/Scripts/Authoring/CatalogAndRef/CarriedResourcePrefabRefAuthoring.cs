using Shared;
using Unity.Entities;
using UnityEngine;

/// <summary>
/// Worker가 운반하는 자원 시각화용 프리팹 참조 Authoring
/// - EntitiesSubScene에 빈 GameObject에 이 컴포넌트를 추가
/// - 인스펙터에서 치즈 프리팹 할당
/// </summary>
public class CarriedResourcePrefabRefAuthoring : MonoBehaviour
{
    [Tooltip("치즈 자원 시각화 프리팹")]
    public GameObject cheesePrefab;

    class Baker : Baker<CarriedResourcePrefabRefAuthoring>
    {
        public override void Bake(CarriedResourcePrefabRefAuthoring authoring)
        {
            if (!authoring.cheesePrefab)
                return;

            // 프리팹을 Entity로 변환 (동적 트랜스폼 사용)
            Entity cheesePrefabEntity = GetEntity(authoring.cheesePrefab, TransformUsageFlags.Dynamic);

            // 싱글톤 엔티티 생성
            Entity singleton = GetEntity(TransformUsageFlags.None);

            AddComponent(singleton, new CarriedResourcePrefabRef
            {
                CheesePrefab = cheesePrefabEntity
            });
        }
    }
}
