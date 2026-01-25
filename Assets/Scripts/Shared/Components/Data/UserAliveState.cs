using Unity.Entities;

namespace Shared
{
    /// <summary>
    /// 유저 생존 상태 (Connection 엔티티에 부착)
    /// Hero 엔티티와 별개로 유지되어 Hero 파괴 후에도 상태 추적 가능
    /// </summary>
    public struct UserAliveState : IComponentData
    {
        public bool IsAlive;
        public Entity HeroEntity;
    }
}
