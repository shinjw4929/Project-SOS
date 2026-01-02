using Unity.Entities;
using UnityEngine;

public class FactionAuthoring : MonoBehaviour
{
    public byte faction = 1;

    class Baker : Baker<FactionAuthoring>
    {
        public override void Bake(FactionAuthoring authoring)
        {
            var e = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(e, new Faction { Value = authoring.faction });
        }
    }
}
