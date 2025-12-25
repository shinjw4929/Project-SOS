using Unity.Entities;
using UnityEngine;
using Shared;

namespace Authoring
{
    /// <summary>
    /// 유닛 체력 설정을 위한 Authoring Component
    /// Inspector에서 Prefab별로 다른 최대 체력을 설정할 수 있습니다.
    /// </summary>
    public class UnitHealthAuthoring : MonoBehaviour
    {
        [Header("체력 설정")]
        [Tooltip("최대 체력 (게임 시작 시 current도 이 값으로 초기화됩니다)")]
        [Min(1f)]
        public float maxHealth = 100f;

        public class Baker : Baker<UnitHealthAuthoring>
        {
            public override void Bake(UnitHealthAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);

                // 초기 체력은 최대 체력과 동일하게 설정
                AddComponent(entity, new UnitHealth
                {
                    current = authoring.maxHealth,
                    max = authoring.maxHealth
                });
            }
        }
    }
}
