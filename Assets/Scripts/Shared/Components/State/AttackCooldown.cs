using Unity.Entities;

namespace Shared
{
    /// <summary>
    /// 공격 쿨다운 타이머
    /// - CombatStats.AttackSpeed 기반으로 다음 공격까지 남은 시간 추적
    /// - 유닛과 적 모두 사용
    /// </summary>
    public struct AttackCooldown : IComponentData
    {
        // 다음 공격까지 남은 시간 (초)
        // 0 이하일 때 공격 가능
        public float RemainingTime;
    }
}
