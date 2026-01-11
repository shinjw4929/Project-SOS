using Unity.Entities;

namespace Shared
{
    /// <summary>
    /// 부모 엔티티에 Selection Ring이 연결되었음을 표시하는 태그
    /// 중복 생성 방지용
    /// </summary>
    public struct SelectionRingLinked : IComponentData { }
}
