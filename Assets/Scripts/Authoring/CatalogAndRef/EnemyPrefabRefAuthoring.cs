using Unity.Entities;
using UnityEngine;
using Shared;

public class EnemyPrefabRefAuthoring : MonoBehaviour
{
    public GameObject enemyPrefab;

    class Baker : Baker<EnemyPrefabRefAuthoring>
    {
        public override void Bake(EnemyPrefabRefAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);

            AddComponent(entity, new EnemyPrefabRef
            {
                Prefab = GetEntity(authoring.enemyPrefab,
                    TransformUsageFlags.Dynamic)
            });
        }
    }
}
