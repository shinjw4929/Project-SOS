using Unity.Entities;

namespace Shared
{
    // 사용자의 조작 맥락을 정의
    public enum UserContext : byte
    {
        Command, // 유닛 선택 및 명령 하달 모드
        Construction, // 건물 배치/건설 모드
        OutGame, // 사망 또는 게임 종료 상태 (조작 불능)
    }
    /// <summary>
    /// 클라이언트 사용자의 전역 상태를 관리하는 싱글톤
    /// </summary>
    public struct UserState : IComponentData
    {
        public UserContext CurrentState;
    }
}