using Unity.Entities;
using UnityEngine;
using Shared;

namespace Authoring
{
    public class UnitEntityReferenceAuthoring : MonoBehaviour
    {
        public GameObject unitPrefabGameObject;

        public class Baker : Baker<UnitEntityReferenceAuthoring>
        {
            public override void Bake(UnitEntityReferenceAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(entity, new EntitiesReferences
                {
                    UnitPrefabEntity = GetEntity(authoring.unitPrefabGameObject, TransformUsageFlags.Dynamic),
                });
            }
        }
    }
}