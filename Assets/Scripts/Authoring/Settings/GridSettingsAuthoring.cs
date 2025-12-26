using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Shared;
using Unity.Collections.LowLevel.Unsafe;// 메모리 바로 접근

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
                int gridX = authoring.gridSize.x;
                int gridZ = authoring.gridSize.y;

                // gridOrigin 계산: Ground 중심 기준 또는 (0,0) 기준
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
                    // Ground가 없으면 원점 중심
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

                // GridCell 버퍼 추가 및 초기화
                var buffer = AddBuffer<GridCell>(entity);
                int totalCells = gridX * gridZ;

                buffer.Length = totalCells;
                
                // 버퍼를 byte 배열처럼 취급하여 포인터를 가져옴
                var rawData = buffer.Reinterpret<byte>().AsNativeArray();
                // UnsafeUtility를 사용해 메모리 전체를 0으로 초기화
                unsafe 
                {
                    UnsafeUtility.MemSet(rawData.GetUnsafePtr(), 0, rawData.Length);
                }
                
                // Legacy: for문 초기화
                // for (int i = 0; i < buffer.Length; i++)
                // {
                //     buffer[i] = new GridCell { isOccupied = false };
                // }
            }
        }
    }
}
