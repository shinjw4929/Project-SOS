using Unity.Entities;
using Unity.Mathematics;

namespace Shared
{
    // 드래그 박스 선택을 위한 데이터
    public struct SelectionBox : IComponentData
    {
        public float2 startScreenPos;
        public float2 currentScreenPos;
        public bool isDragging;
    }
}