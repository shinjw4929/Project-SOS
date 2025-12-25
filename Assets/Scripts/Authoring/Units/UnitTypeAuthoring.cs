using Unity.Entities;
using UnityEngine;
using Shared;

namespace Authoring
{
    public class UnitTypeAuthoring : MonoBehaviour
    {
        [Header("유닛 타입")]
        public UnitTypeEnum unitType = UnitTypeEnum.Infantry;

        public class Baker : Baker<UnitTypeAuthoring>
        {
            public override void Bake(UnitTypeAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(entity, new UnitType
                {
                    type = authoring.unitType
                });
            }
        }
    }
}
