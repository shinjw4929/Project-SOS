using Unity.Entities;
using Unity.NetCode;

namespace Shared
{
    /// <summary>
    /// 유저별 테크 해금 상태를 추적하는 컴포넌트
    /// UserEconomy 엔티티에 함께 저장됨 (GhostOwner로 소유자 구분)
    /// </summary>
    [GhostComponent]
    public struct UserTechState : IComponentData
    {
        /// <summary>
        /// ResourceCenter 보유 여부 (Barracks 해금 조건)
        /// 서버에서 건물 생성/파괴 시 업데이트됨
        /// </summary>
        [GhostField] public bool HasResourceCenter;
    }
}
