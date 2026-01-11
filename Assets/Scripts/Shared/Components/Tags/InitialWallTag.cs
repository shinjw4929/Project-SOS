using Unity.Entities;

namespace Shared
{
    /// <summary>
    /// 서브씬에 초기 배치된 벽을 식별하는 태그.
    /// 게임 중 건설된 벽에는 이 태그가 없음.
    /// </summary>
    public struct InitialWallTag : IComponentData { }
}
