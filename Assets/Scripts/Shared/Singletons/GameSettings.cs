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
    }
}
