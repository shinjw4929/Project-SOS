using Unity.Entities;
using UnityEngine;
using Shared;

namespace Authoring
{
    public class BuildingAuthoring : MonoBehaviour
    {
        public BuildingTypeEnum buildingType = BuildingTypeEnum.Wall;

        public class Baker : Baker<BuildingAuthoring>
        {
            public override void Bake(BuildingAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);

                AddComponent(entity, new Building
                {
                    buildingType = authoring.buildingType,
                    ownerTeamId = 0,
                    gridX = 0,
                    gridY = 0
                });
            }
        }
    }
}
