using Unity.Entities;

namespace Client
{
    /// <summary>
    /// 메시 엔티티 -> 루트 엔티티(VATAnimationState 보유) 참조
    /// - TeamColorTarget과 동일 패턴
    /// - VATAnimationInitSystem에서 Parent 체인 탐색 후 ECB로 부착
    /// </summary>
    public struct VATAnimTarget : IComponentData
    {
        public Entity RootEntity;
    }
}
