using Unity.Entities;
using UnityEngine;
using Shared;

namespace Authoring
{
    public class BuildingEntitiesReferencesAuthoring : MonoBehaviour
    {
        public GameObject wallPrefab;
        public GameObject barracksPrefab;

        public class Baker : Baker<BuildingEntitiesReferencesAuthoring>
        {
            public override void Bake(BuildingEntitiesReferencesAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new BuildingEntitiesReferences
                {
                    wallPrefabEntity = GetEntity(authoring.wallPrefab, TransformUsageFlags.Dynamic),
                    barracksPrefabEntity = GetEntity(authoring.barracksPrefab, TransformUsageFlags.Dynamic)
                });
            }
        }
    }
}