using Unity.Entities;
using Unity.NetCode;
using Unity.Mathematics;

namespace Shared

{
    /// <summary>
    /// 유닛의 행동 의도
    /// - 서버가 판단한 "무엇을 하려는가"를 표현
    /// - 서버가 사용자의 명령을 수신하고, "이 유닛은 이제 공격 상태다/이동 상태다"라고 판단
    /// </summary>

    [GhostComponent]
    public struct UnitIntentState : IComponentData
    {
        [GhostField] public Intent State;
        [GhostField] public Entity TargetEntity;
        [GhostField] public float3 TargetLastKnownPos;
    }

    // '서버가 판단한 유닛의 의도'를 나타냄
    public enum Intent : byte
    {
        Idle = 0,       // 기본, 정지 (자동 타겟팅 활성화)
        Move = 1,       // 땅 찍고 이동 중 (적 무시, 후퇴 보장)
        Hold = 2,       // 위치 사수
        Patrol = 3,     // 순찰
        Build = 4,      // 건설하러 가는 중 + 건설 중
        Gather = 5,     // 캐러 가는 중 + 캐는 중
        Attack = 6,     // 공격하러 가는 중(추격) + 공격 중
        AttackMove = 7, // 공격 이동 (이동 중 적 발견 시 교전)
    }
}