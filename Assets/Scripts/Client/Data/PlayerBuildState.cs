using Unity.Entities;

namespace Client
{
    /// <summary>
    /// 로컬 클라이언트의 건설 모드 상태를 저장하는 싱글톤
    /// - B키로 건설 모드 토글 (PlayerWorkSwitchingSystem)
    /// - 건설 모드일 때만 건물 배치 입력 처리
    /// - 클라이언트 전역 상태 (특정 플레이어 엔티티와 무관)
    /// </summary>
    public struct PlayerBuildState : IComponentData
    {
        /// <summary>현재 건설 모드 활성화 여부</summary>
        public bool isBuildMode;
    }
}