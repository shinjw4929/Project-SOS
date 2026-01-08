using Unity.Entities;
using Unity.NetCode;

namespace Shared

{
    /// <summary>
    /// 유닛의 동작을 나타내는 State
    /// - 애니메이션 및 물리 이동 시스템이 참조
    /// </summary>
    [GhostComponent]
    public struct UnitActionState : IComponentData
    {
        [GhostField] public Action State;
    }

    // 오직 '유닛이 어떻게 행동하고있는가?'만 나타냄
    public enum Action : byte
    {
        // 1. 기본 상태
        Idle = 0,        // 가만히 서 있음 (대기, Hold, Stop 상태일 때의 모습)
        Moving = 1,      // 이동 중 (걷기/뛰기 애니메이션)
        
        // 2. 상호작용
        Working = 2,     // 채집, 건설, 수리 등 (작업 애니메이션)
        Attacking = 3,   // 공격 모션 수행 중 (공격 애니메이션)
        
        // 3. 특수 상태
        // Stop -> 삭제됨 (Intent.None + Action.Idle로 표현)
        // Holding -> 삭제됨 (Intent.Hold + Action.Idle로 표현)
        
        Disabled = 200,  // 기절/마비 (Stunned 애니메이션)
        Dying = 254,     // 사망 연출 중
        Dead = 255       // 사망 완료 (시체)
    }
}