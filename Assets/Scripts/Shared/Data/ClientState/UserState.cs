using Unity.Entities;

namespace Shared
{
    /// <summary>
    /// 클라이언트 사용자의 전역 상태를 관리하는 싱글톤
    /// </summary>
    public struct UserState : IComponentData
    {
        public UserContext CurrentState;
    }
    
    // 사용자의 조작 맥락을 정의
    public enum UserContext : byte
    {
        Command = 0,        // 유닛 선택 및 명령 하달 모드
        BuildMenu = 1,      // 건설 메뉴가 열린 상태 (유닛 Q)
        Construction = 2,   // 건물 배치/건설 모드
        StructureMenu = 3,  // 건물 명령 메뉴 (건물 Q)
        Dead = 255,         // 사망 또는 게임 종료 상태 (조작 불능)
    }
}