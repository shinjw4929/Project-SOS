using Unity.Entities;

namespace Client
{
    // 현재 선택 모드를 저장하는 싱글톤
    public struct SelectionState : IComponentData
    {
        public SelectionMode mode;
    }

    public enum SelectionMode : byte
    {
        Idle = 0,
        SingleClick = 1,
        BoxDragging = 2,
    }
}