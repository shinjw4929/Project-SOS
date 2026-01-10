using Unity.Entities;

namespace Shared
{
    public struct GatheringAbility : IComponentData
    {
        // 1. 최대 운반 할 수 있는 양
        public int MaxCarryAmount;
        
        // 2. [추가] 채집 속도 (기본 1.0)
        // 값이 클수록 빨리 캠 (예: 2.0이면 2배 속도)
        public float GatheringSpeed;
        public float UnloadDuration;
    }
}