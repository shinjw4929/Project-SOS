using Unity.Entities;
using Unity.NetCode;

namespace Shared
{
    // 유닛의 현재 행동 상태 (Enum)
    public enum UnitContext : byte
    {
        Idle = 0,           // 대기
        Moving = 1,         // 이동 중
        Attacking = 2,      // 공격 중 (쿨타임 대기 포함)
        Chasing = 3,        // 적 추격 중
        Gathering = 10,      // 자원 채집 중 (이동 -> 채집 -> 반납 사이클)
        Constructing = 11,   // 건설 중 (일꾼이 건물 짓는 모션)
        Disabled = 20,
        Dying = 254,
        Dead = 255          // 사망
    }

    [GhostComponent]
    public struct UnitState : IComponentData
    {
        // 1. 현재 상태 (Enum)
        [GhostField] public UnitContext CurrentState;
        
        // 3. (선택) 상태 보조 데이터 (예: 채집 시작한지 얼마나 됐나?)
        // 복잡한 타이머는 별도 컴포넌트로 빼도 되지만, 간단한 건 여기 둬도 됨
        // [GhostField] public double StateStartTime; 
    }
}