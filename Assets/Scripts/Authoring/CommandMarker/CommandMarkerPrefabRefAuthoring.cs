using Unity.Entities;
using UnityEngine;

namespace Authoring
{
    public class CommandMarkerPrefabRefAuthoring : MonoBehaviour
    {
        [Header("Command Marker Prefabs")]
        public GameObject moveMarkerPrefab;
        public GameObject attackMarkerPrefab;
        public GameObject gatherMarkerPrefab;

        class Baker : Baker<CommandMarkerPrefabRefAuthoring>
        {
            public override void Bake(CommandMarkerPrefabRefAuthoring authoring)
            {
                Entity singleton = GetEntity(TransformUsageFlags.None);

                var prefabRef = new Shared.CommandMarkerPrefabRef();

                if (authoring.moveMarkerPrefab != null)
                    prefabRef.MoveMarkerPrefab = GetEntity(authoring.moveMarkerPrefab, TransformUsageFlags.Dynamic);

                if (authoring.attackMarkerPrefab != null)
                    prefabRef.AttackMarkerPrefab = GetEntity(authoring.attackMarkerPrefab, TransformUsageFlags.Dynamic);

                if (authoring.gatherMarkerPrefab != null)
                    prefabRef.GatherMarkerPrefab = GetEntity(authoring.gatherMarkerPrefab, TransformUsageFlags.Dynamic);

                AddComponent(singleton, prefabRef);
            }
        }
    }
}
