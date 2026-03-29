using Unity.Entities;

namespace Server
{
    /// <summary>
    /// TokenValidationSystem이 토큰 검증 성공 시 RPC 엔티티에 부착하는 태그.
    /// GoInGameServerSystem이 WithAll&lt;TokenValidatedTag&gt;로 필터링한다.
    /// </summary>
    public struct TokenValidatedTag : IComponentData { }
}
