using Unity.Entities;
using UnityEngine;

public class EnemyAuthoring : MonoBehaviour
{
    public float moveSpeed = 3f;
    public float loseTargetDistance = 15f;

    class Baker : Baker<EnemyAuthoring>
    {
        public override void Bake(EnemyAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new EnemyFollowConfig
            {
                MoveSpeed = authoring.moveSpeed,
                LoseTargetDistance = authoring.loseTargetDistance
            });

            AddComponent(entity, new EnemyTarget
            {
                HasTarget = false,
                TargetEntity = Entity.Null,
                LastKnownPosition = default
            });
        }
    }
}
