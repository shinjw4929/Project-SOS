using Unity.Entities;

namespace Shared
{
    /// <summary>
    /// Wave 단계 열거형.
    /// </summary>
    public enum WavePhase : byte
    {
        Wave0 = 0,  // 초기 상태: EnemyBig만 스폰
        Wave1 = 1,  // EnemySmall 추가 스폰 시작
        Wave2 = 2   // EnemyFlying 추가 스폰 시작
    }

    /// <summary>
    /// 게임 진행 상태 싱글톤.
    /// Wave 전환 조건 및 처치 수 추적.
    /// </summary>
    public struct GamePhaseState : IComponentData
    {
        /// <summary>
        /// 현재 Wave 단계
        /// </summary>
        public WavePhase CurrentWave;

        /// <summary>
        /// 게임 시작 후 경과 시간 (초)
        /// </summary>
        public float ElapsedTime;

        /// <summary>
        /// 총 적 처치 수
        /// </summary>
        public int TotalKillCount;

        /// <summary>
        /// Wave0에서 스폰한 적 수 (초기 스폰 완료 추적)
        /// </summary>
        public int Wave0SpawnedCount;

        /// <summary>
        /// 마지막 주기적 스폰 이후 경과 시간
        /// </summary>
        public float SpawnTimer;
    }
}
