using Unity.Entities;

namespace Shared
{
    /// <summary>
    /// Worker가 운반 중인 자원 시각화 엔티티 태그
    /// - Parent 컴포넌트로 Worker에 연결됨
    /// - Worker 사망 시 함께 삭제됨
    /// </summary>
    public struct CarriedResourceTag : IComponentData { }
}
