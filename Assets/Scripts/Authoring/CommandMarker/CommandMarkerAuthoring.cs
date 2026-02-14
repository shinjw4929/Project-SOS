using Unity.Entities;
using UnityEngine;

namespace Authoring
{
    public class CommandMarkerAuthoring : MonoBehaviour
    {
        public class Baker : Baker<CommandMarkerAuthoring>
        {
            public override void Bake(CommandMarkerAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);

                AddComponent<Shared.CommandMarkerTag>(entity);
                AddComponent(entity, new Shared.CommandMarkerLifetime
                {
                    TotalTime = 1.0f,
                    RemainingTime = 1.0f,
                    InitialScale = 2.0f
                });
            }
        }
    }
}
