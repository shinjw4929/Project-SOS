using Unity.Entities;
using UnityEngine;

namespace Authoring
{
    public class PlayerEntityReferenceAuthoring : MonoBehaviour
    {
        public GameObject playerPrefabGameObject;

        public class Baker : Baker<PlayerEntityReferenceAuthoring>
        {
            public override void Bake(PlayerEntityReferenceAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(entity, new EntitiesReferences
                {
                    playerPrefabEntity = GetEntity(authoring.playerPrefabGameObject, TransformUsageFlags.Dynamic),
                });
            }
        }
    }
}