using Unity.Entities;
using UnityEngine;
using Shared;

namespace Authoring
{
    /// <summary>
    /// 적 프리팹 카탈로그 Authoring.
    /// EntitiesSubScene에 배치하여 적 프리팹 관리.
    /// 각 타입별 명시적 필드로 순서 종속성 없음.
    /// </summary>
    public class EnemyPrefabCatalogAuthoring : MonoBehaviour
    {
        [Header("Enemy Prefabs")]
        [Tooltip("작고 빠른 적")]
        public GameObject smallPrefab;

        [Tooltip("크고 강한 적")]
        public GameObject bigPrefab;

        [Tooltip("비행 적 (벽 무시)")]
        public GameObject flyingPrefab;

        public class Baker : Baker<EnemyPrefabCatalogAuthoring>
        {
            public override void Bake(EnemyPrefabCatalogAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.None);

                AddComponent(entity, new EnemyPrefabCatalog
                {
                    SmallPrefab = authoring.smallPrefab != null
                        ? GetEntity(authoring.smallPrefab, TransformUsageFlags.Dynamic)
                        : Entity.Null,
                    BigPrefab = authoring.bigPrefab != null
                        ? GetEntity(authoring.bigPrefab, TransformUsageFlags.Dynamic)
                        : Entity.Null,
                    FlyingPrefab = authoring.flyingPrefab != null
                        ? GetEntity(authoring.flyingPrefab, TransformUsageFlags.Dynamic)
                        : Entity.Null
                });
            }
        }
    }
}
