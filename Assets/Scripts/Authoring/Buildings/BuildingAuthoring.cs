using Unity.Entities;
using UnityEngine;
using Shared;

namespace Authoring
{
    public class BuildingAuthoring : MonoBehaviour
    {
        public BuildingTypeEnum buildingType = BuildingTypeEnum.Wall;
        public int gridWidth = 1;
        public int gridHeight = 1;

        public class Baker : Baker<BuildingAuthoring>
        {
            public override void Bake(BuildingAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);

                AddComponent(entity, new Building
                {
                    buildingType = authoring.buildingType,
                    ownerTeamId = 0
                });

                AddComponent(entity, new GridOccupancy
                {
                    gridX = 0,
                    gridY = 0,
                    width = authoring.gridWidth,
                    height = authoring.gridHeight
                });
            }
        }
    }
}
