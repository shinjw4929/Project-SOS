using Unity.Entities;
using UnityEngine;

namespace Authoring
{
    /// <summary>
    /// Selection Ring 프리팹 참조 싱글톤 Authoring (팀별 프리팹)
    /// </summary>
    public class SelectionRingPrefabRefAuthoring : MonoBehaviour
    {
        [Header("팀별 Selection Ring 프리팹")]
        public GameObject allyRingPrefab;    // 아군 (초록)
        public GameObject enemyRingPrefab;   // 적 (빨강)
        public GameObject neutralRingPrefab; // 중립 (노랑)

        class Baker : Baker<SelectionRingPrefabRefAuthoring>
        {
            public override void Bake(SelectionRingPrefabRefAuthoring authoring)
            {
                Entity singleton = GetEntity(TransformUsageFlags.None);

                var prefabRef = new Shared.SelectionRingPrefabRef();

                if (authoring.allyRingPrefab != null)
                    prefabRef.AllyRingPrefab = GetEntity(authoring.allyRingPrefab, TransformUsageFlags.Dynamic);

                if (authoring.enemyRingPrefab != null)
                    prefabRef.EnemyRingPrefab = GetEntity(authoring.enemyRingPrefab, TransformUsageFlags.Dynamic);

                if (authoring.neutralRingPrefab != null)
                    prefabRef.NeutralRingPrefab = GetEntity(authoring.neutralRingPrefab, TransformUsageFlags.Dynamic);

                // 하위 호환: AllyRingPrefab을 기본으로 사용
                prefabRef.RingPrefab = prefabRef.AllyRingPrefab;

                AddComponent(singleton, prefabRef);
            }
        }
    }
}
