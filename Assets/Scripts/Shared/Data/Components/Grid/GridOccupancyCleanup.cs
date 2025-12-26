using Unity.Entities;
using Unity.Mathematics;

namespace Shared
{
    // 건물이 파괴되어 GridPosition이 사라졌을 때, 
    // "내가 어디에 있었는지" 기억해서 그리드를 비우기 위한 백업 데이터
    public struct GridOccupancyCleanup : ICleanupComponentData
    {
        public int2 GridPosition;
        public int Width;
        public int Length;
    }
}