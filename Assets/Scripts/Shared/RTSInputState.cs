using Unity.Entities;
using Unity.Mathematics;

namespace Shared // Client -> Shared로 변경
{
    // 데이터 구조체는 Shared에 두는 것이 참조 관계상 편합니다.
    public struct RTSInputState : IComponentData
    {
        public float3 targetPosition;
        public bool hasTarget;
    }
}