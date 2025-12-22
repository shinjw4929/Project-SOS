using Unity.Entities;

namespace Shared
{
    // 선택 상태를 나타내는 태그 컴포넌트
    // IEnableableComponent로 빠른 On/Off 토글 가능
    public struct Selected : IComponentData, IEnableableComponent
    {
    }
}