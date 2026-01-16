using Unity.Entities;

namespace Shared
{
    /// <summary>
    /// 물리 충돌 반경 (Collider 기반)
    /// - 건물 충돌 및 공격 판정에 사용
    /// - Authoring 시 Collider에서 자동 추출
    /// </summary>
    public struct PhysicsRadius : IComponentData
    {
        public float Value;
    }
}
