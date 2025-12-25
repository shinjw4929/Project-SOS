using Unity.Entities;
using UnityEngine;

public class EnemyGhostPrefabAuthoring : MonoBehaviour
{
    public GameObject enemyPrefab;

    class Baker : Baker<EnemyGhostPrefabAuthoring>
    {
        public override void Bake(EnemyGhostPrefabAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);

            AddComponent(entity, new EnemyGhostPrefab
            {
                Prefab = GetEntity(authoring.enemyPrefab,
                    TransformUsageFlags.Dynamic)
            });
        }
    }
}
