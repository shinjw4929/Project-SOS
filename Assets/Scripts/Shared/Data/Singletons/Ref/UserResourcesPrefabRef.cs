using Unity.Entities;

namespace Shared
{
    /// <summary>
    /// UserResources 프리팹 엔티티를 담아두는 싱글톤 컴포넌트.
    /// GoInGameServerSystem에서 플레이어 진입 시 이 프리팹을 Instantiate하여 자원 엔티티를 생성한다.
    /// </summary>
    public struct UserResourcesPrefabRef : IComponentData
    {
        public Entity Prefab;
    }
}
