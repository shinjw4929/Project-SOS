using Unity.Entities;
using UnityEngine;
using Shared;

namespace Authoring
{
    /// <summary>
    /// 적 스폰 포인트 Authoring.
    /// EntitiesSubScene에 빈 GameObject로 배치하여 스폰 위치 지정.
    /// 여러 개를 배치하면 랜덤하게 선택되어 스폰됨.
    /// </summary>
    public class EnemySpawnPointAuthoring : MonoBehaviour
    {
        public class Baker : Baker<EnemySpawnPointAuthoring>
        {
            public override void Bake(EnemySpawnPointAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.None);

                AddComponent(entity, new EnemySpawnPoint
                {
                    Position = authoring.transform.position
                });
            }
        }
    }
}
