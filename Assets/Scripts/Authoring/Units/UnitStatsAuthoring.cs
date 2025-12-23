using Unity.Entities;
using UnityEngine;
using Shared;

namespace Authoring
{
    /// <summary>
    /// 유닛 기본 스탯 설정을 위한 Authoring Component
    /// Inspector에서 Prefab별로 다른 이동속도와 공격력을 설정할 수 있습니다.
    /// </summary>
    public class UnitStatsAuthoring : MonoBehaviour
    {
        [Header("이동 설정")]
        [Tooltip("이동 속도 (units per second)")]
        [Min(0.1f)]
        public float moveSpeed = 10f;

        [Header("전투 설정")]
        [Tooltip("공격력 (데미지)")]
        [Min(0f)]
        public float attackPower = 25f;

        public class Baker : Baker<UnitStatsAuthoring>
        {
            public override void Bake(UnitStatsAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);

                AddComponent(entity, new UnitStats
                {
                    moveSpeed = authoring.moveSpeed,
                    attackPower = authoring.attackPower
                });
            }
        }
    }
}
