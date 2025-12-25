using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Shared;

namespace Authoring
{
    public class GridSettingsAuthoring : MonoBehaviour
    {
        public float cellSize = 1.0f;
        public Vector2 gridOrigin = Vector2.zero;

        public class Baker : Baker<GridSettingsAuthoring>
        {
            public override void Bake(GridSettingsAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new GridSettings
                {
                    cellSize = authoring.cellSize,
                    gridOrigin = new float2(authoring.gridOrigin.x, authoring.gridOrigin.y)
                });
            }
        }
    }
}
