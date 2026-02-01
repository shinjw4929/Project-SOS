using Unity.Entities;

namespace Shared
{
    /// <summary>
    /// 게임 운영 관련 설정 싱글톤.
    /// 씬에 GameSettingsAuthoring을 배치하여 설정값 관리.
    /// </summary>
    public struct GameSettings : IComponentData
    {
        /// <summary>
        /// 초기 배치 벽이 자동 파괴되기까지 걸리는 시간 (초)
        /// </summary>
        public float InitialWallDecayTime;

        // === Wave 설정 ===

        /// <summary>
        /// Wave0 초기 스폰 적 수 (기본값: 30)
        /// </summary>
        public int Wave0InitialSpawnCount;

        /// <summary>
        /// Wave1 전환 시간 (초) (기본값: 60)
        /// </summary>
        public float Wave1TriggerTime;

        /// <summary>
        /// Wave1 전환 처치 수 (기본값: 15)
        /// </summary>
        public int Wave1TriggerKillCount;

        /// <summary>
        /// Wave2 전환 시간 (초) (기본값: 120)
        /// </summary>
        public float Wave2TriggerTime;

        /// <summary>
        /// Wave2 전환 처치 수 (기본값: 30)
        /// </summary>
        public int Wave2TriggerKillCount;

        /// <summary>
        /// Wave1 적 스폰 주기 (초) (기본값: 5)
        /// </summary>
        public float Wave1SpawnInterval;

        /// <summary>
        /// Wave1 1회 스폰 수 (기본값: 3)
        /// </summary>
        public int Wave1SpawnCount;

        /// <summary>
        /// Wave2 적 스폰 주기 (초) (기본값: 4)
        /// </summary>
        public float Wave2SpawnInterval;

        /// <summary>
        /// Wave2 1회 스폰 수 (기본값: 4)
        /// </summary>
        public int Wave2SpawnCount;

        /// <summary>
        /// 맵에 존재할 수 있는 최대 적 수 (기본값: 1200)
        /// </summary>
        public int MaxEnemyCount;
    }
}
