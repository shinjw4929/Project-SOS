using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Shared;

namespace Authoring
{
    public class GridSettingsAuthoring : MonoBehaviour
    {
        public float cellSize = 1.0f;
        public int gridWidth = 100;
        public int gridHeight = 100;
        public Transform groundTransform;

        public class Baker : Baker<GridSettingsAuthoring>
        {
            public override void Bake(GridSettingsAuthoring authoring)
            {
                int gridWidth = authoring.gridWidth;
                int gridHeight = authoring.gridHeight;

                // gridOrigin 계산: Ground 중심 기준 또는 (0,0) 기준
                float2 gridOrigin;
                if (authoring.groundTransform != null)
                {
                    Vector3 groundPos = authoring.groundTransform.position;
                    float totalWidth = gridWidth * authoring.cellSize;
                    float totalHeight = gridHeight * authoring.cellSize;
                    gridOrigin = new float2(
                        groundPos.x - totalWidth / 2f,
                        groundPos.z - totalHeight / 2f
                    );
                }
                else
                {
                    // Ground가 없으면 원점 중심
                    float totalWidth = gridWidth * authoring.cellSize;
                    float totalHeight = gridHeight * authoring.cellSize;
                    gridOrigin = new float2(-totalWidth / 2f, -totalHeight / 2f);
                }

                Entity entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new GridSettings
                {
                    cellSize = authoring.cellSize,
                    gridOrigin = gridOrigin,
                    gridWidth = gridWidth,
                    gridHeight = gridHeight
                });

                // GridCell 버퍼 추가 및 초기화
                var buffer = AddBuffer<GridCell>(entity);
                int totalCells = gridWidth * gridHeight;
                buffer.Length = totalCells;
                for (int i = 0; i < totalCells; i++)
                {
                    buffer[i] = new GridCell { isOccupied = false };
                }
            }
        }
    }
}
