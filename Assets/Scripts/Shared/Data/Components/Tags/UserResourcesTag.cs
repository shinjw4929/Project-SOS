using Unity.Entities;
using Unity.NetCode;

namespace Shared
{
    /// <summary>
    /// 플레이어별 자원 엔티티를 식별하기 위한 태그
    /// GhostOwner와 함께 사용하여 소유자별 자원을 구분
    /// </summary>
    [GhostComponent]
    public struct UserResourcesTag : IComponentData { }
}
