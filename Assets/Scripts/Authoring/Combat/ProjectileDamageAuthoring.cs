using Unity.Entities;
using UnityEngine;

public class ProjectileDamageAuthoring : MonoBehaviour
{
    public int defaultDamage = 1;

    class Baker : Baker<ProjectileDamageAuthoring>
    {
        public override void Bake(ProjectileDamageAuthoring authoring)
        {
            var e = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(e, new ProjectileDamageData { Value = authoring.defaultDamage });
            AddComponent(e, new ProjectileOwnerFaction { Value = 0 });
        }
    }
}
