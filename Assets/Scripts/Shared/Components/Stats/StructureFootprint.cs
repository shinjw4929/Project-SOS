using Unity.Entities;

namespace Shared
{
    // 건물이 차지하는 그리드 칸 수
    public struct StructureFootprint : IComponentData
    { 
        public int Width;  // 가로 길이
        public int Length; // 세로 길이
        public float Height; // 건물 높이
        // 센터 포지션 계산을 위한 오프셋을 미리 계산해 둘 수도 있음
    }
}