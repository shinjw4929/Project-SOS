using Unity.Entities;
using UnityEngine;
using Shared;

namespace Authoring
{
    public class ResourceNodePrefabRefAuthoring : MonoBehaviour
    {
        public GameObject resourceNodePrefab;

        class Baker : Baker<ResourceNodePrefabRefAuthoring>
        {
            public override void Bake(ResourceNodePrefabRefAuthoring authoring)
            {
                if (!authoring.resourceNodePrefab)
                {
                    return;
                }

                // 프리팹을 Entity로 변환 (위치 불필요)
                Entity prefabEntity = GetEntity(authoring.resourceNodePrefab, TransformUsageFlags.None);

                // 싱글톤 엔티티 생성
                Entity singleton = GetEntity(TransformUsageFlags.None);

                AddComponent(singleton, new ResourceNodePrefabRef
                {
                    Prefab = prefabEntity
                });
            }
        }
    }
}