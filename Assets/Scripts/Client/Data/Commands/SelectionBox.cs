using Unity.Entities;
using Unity.Mathematics;

namespace Client
{
    // 드래그 박스 선택을 위한 싱글톤
    public struct SelectionBox : IComponentData
    {
        public float2 StartScreenPos;
        public float2 CurrentScreenPos;
        public bool IsDragging;
    }
}