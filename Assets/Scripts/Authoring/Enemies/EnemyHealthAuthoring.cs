using Unity.Entities;
using UnityEngine;

public class EnemyHealthAuthoring : MonoBehaviour
{
    public int maxHp = 100;

    class Baker : Baker<EnemyHealthAuthoring>
    {
        public override void Bake(EnemyHealthAuthoring authoring)
        {
            Entity e = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(e, new EnemyHealthData
            {
                Max = authoring.maxHp,
                Current = authoring.maxHp
            });
        }
    }
}
