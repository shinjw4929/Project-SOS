using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Shared;

public class ProjectileAuthoring : MonoBehaviour
{
    [Header("Movement")]
    public float speed = 20f;
    public float maxDistance = 30f;

    [Header("Damage")]
    public int attackPower = 10;

    class Baker : Baker<ProjectileAuthoring>
    {
        public override void Bake(ProjectileAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic | TransformUsageFlags.Renderable);

            AddComponent(entity, new ProjectileTag());
            AddComponent(entity, new ProjectileMove
            {
                Direction = float3.zero,
                Speed = authoring.speed,
                RemainingDistance = authoring.maxDistance
            });
            AddComponent(entity, new CombatStats
            {
                AttackPower = authoring.attackPower,
                AttackSpeed = 1.0f,
                AttackRange = 3.0f
            });
        }
    }
}
