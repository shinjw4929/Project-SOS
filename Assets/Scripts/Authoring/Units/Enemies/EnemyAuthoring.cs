using Unity.Entities;
using UnityEngine;
using Shared;

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

            // 적 팀 ID (플레이어 teamId=0과 다른 값)
            AddComponent(entity, new Team { teamId = -1 });
        }
    }
}
