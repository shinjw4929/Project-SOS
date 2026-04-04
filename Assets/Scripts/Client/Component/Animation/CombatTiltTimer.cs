using Unity.Entities;

namespace Client
{
    /// <summary>
    /// 공격 기울임 swing-return 사이클 타이머 (클라이언트 전용 시각효과)
    /// </summary>
    public struct CombatTiltTimer : IComponentData
    {
        public float Timer;           // 공격 사이클 타이머
        public float CurrentTilt;     // 현재 기울기 팩터 (0~1)
        public byte WasAttacking;     // 이전 프레임 Attacking 여부
        public float DeathTiltElapsed; // 사망 기울임 경과 시간
        public byte WasDying;         // Dying 상태 첫 프레임 감지용
    }
}
