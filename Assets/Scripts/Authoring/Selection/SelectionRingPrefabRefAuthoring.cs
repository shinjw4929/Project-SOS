using Unity.Entities;
using UnityEngine;

namespace Authoring
{
    /// <summary>
    /// Selection Ring 프리팹 참조 싱글톤 Authoring
    /// </summary>
    public class SelectionRingPrefabRefAuthoring : MonoBehaviour
    {
        public GameObject selectionRingPrefab;

        class Baker : Baker<SelectionRingPrefabRefAuthoring>
        {
            public override void Bake(SelectionRingPrefabRefAuthoring authoring)
            {
                if (authoring.selectionRingPrefab == null) return;

                Entity prefabEntity = GetEntity(authoring.selectionRingPrefab, TransformUsageFlags.Dynamic);
                Entity singleton = GetEntity(TransformUsageFlags.None);

                AddComponent(singleton, new Shared.SelectionRingPrefabRef
                {
                    RingPrefab = prefabEntity
                });
            }
        }
    }
}
