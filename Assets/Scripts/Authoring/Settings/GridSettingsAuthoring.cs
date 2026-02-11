using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Shared;
using Unity.Collections.LowLevel.Unsafe;

namespace Authoring
{
    public class GridSettingsAuthoring : MonoBehaviour
    {
        public float cellSize = 1.0f;
        public int2 gridSize = new int2(100, 100);
        public Transform groundTransform;

        public class Baker : Baker<GridSettingsAuthoring>
        {
            public override void Bake(GridSettingsAuthoring authoring)
            {
                int gridX, gridZ;
                if (authoring.groundTransform != null)
                {
                    // Unity Plane: localScale * 10 = 실제 월드 크기
                    Vector3 scale = authoring.groundTransform.localScale;
                    gridX = Mathf.RoundToInt(scale.x * 10f / authoring.cellSize);
                    gridZ = Mathf.RoundToInt(scale.z * 10f / authoring.cellSize);
                }
                else
                {
                    // groundTransform 미할당 시 인스펙터 필드값 사용
                    gridX = authoring.gridSize.x;
                    gridZ = authoring.gridSize.y;
                }

                // gridOrigin 계산: Ground 중심 기준 또는 원점 기준
                float2 gridOrigin;
                if (authoring.groundTransform != null)
                {
                    Vector3 groundPos = authoring.groundTransform.position;
                    float totalX = gridX * authoring.cellSize;
                    float totalZ = gridZ * authoring.cellSize;
                    gridOrigin = new float2(
                        groundPos.x - totalX / 2f,
                        groundPos.z - totalZ / 2f
                    );
                }
                else
                {
                    float totalX = gridX * authoring.cellSize;
                    float totalZ = gridZ * authoring.cellSize;
                    gridOrigin = new float2(-totalX / 2f, -totalZ / 2f);
                }

                Entity entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new GridSettings
                {
                    CellSize = authoring.cellSize,
                    GridOrigin = gridOrigin,
                    GridSize = new int2(gridX, gridZ),
                });

                var buffer = AddBuffer<GridCell>(entity);
                int totalCells = gridX * gridZ;

                buffer.Length = totalCells;

                var rawData = buffer.Reinterpret<byte>().AsNativeArray();
                unsafe
                {
                    UnsafeUtility.MemSet(rawData.GetUnsafePtr(), 0, rawData.Length);
                }
            }
        }
    }
}
