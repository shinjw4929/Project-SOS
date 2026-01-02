using Unity.Entities;
using UnityEngine;

public class AttackDamageAuthoring : MonoBehaviour
{
    public int damage = 1;

    class Baker : Baker<AttackDamageAuthoring>
    {
        public override void Bake(AttackDamageAuthoring authoring)
        {
            var e = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(e, new AttackDamage { Value = authoring.damage });
        }
    }
}
