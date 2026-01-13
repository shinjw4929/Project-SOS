using Unity.Entities;

namespace Shared
{
    /// <summary>
    /// Y축 위치 고정용 컴포넌트
    /// - 스폰 시 초기 y 좌표를 저장
    /// - 물리 충돌로 인한 y축 떠오름 방지
    /// </summary>
    public struct LockedYPosition : IComponentData
    {
        public float Value;
    }
}
