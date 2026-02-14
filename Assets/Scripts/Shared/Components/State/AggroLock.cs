using Unity.Entities;

namespace Shared
{
    /// <summary>
    /// 어그로 고정 컴포넌트
    /// - 피격 시 N초 동안 어그로 대상 변경 불가
    /// - RemainingLockTime이 0보다 크면 어그로 고정 상태
    /// </summary>
    public struct AggroLock : IComponentData
    {
        public Entity LockedTarget;       // 고정된 타겟
        public float RemainingLockTime;   // 고정 남은 시간
        public float LockDuration;        // 기본 고정 시간 (초)
    }
}
