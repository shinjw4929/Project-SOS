using Unity.Entities;
using Unity.Mathematics;

namespace Client
{
    /// <summary>
    /// 유닛/건물 선택 상태를 관리하는 싱글톤
    /// - 마우스 입력 상태 (Phase)
    /// - 드래그 박스 좌표
    /// </summary>
    public struct SelectionState : IComponentData
    {
        // 현재 선택 Phase
        public SelectionPhase Phase;

        // 드래그 박스 좌표 (화면 좌표)
        public float2 StartScreenPos;
        public float2 CurrentScreenPos;
    }

    /// <summary>
    /// 선택 처리의 Phase (상태 머신)
    /// - Idle: 대기
    /// - Pressing: 마우스 누른 직후 (클릭/드래그 구분 전)
    /// - Dragging: 드래그 중 (박스 렌더링)
    /// - PendingClick: 단일 클릭 처리 대기
    /// - PendingBox: 박스 선택 처리 대기
    /// </summary>
    public enum SelectionPhase : byte
    {
        Idle = 0,
        Pressing = 1,
        Dragging = 2,
        PendingClick = 3,
        PendingBox = 4,
    }
}