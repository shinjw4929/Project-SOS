using Unity.Entities;

namespace Shared
{
    /// <summary>
    /// 자원 노드의 고정 속성
    /// </summary>
    public struct ResourceNodeSetting : IComponentData
    {
        // 자원 종류
        public ResourceType ResourceType;
        // 1회 채집 시 캐는 양 (예: 8)
        public int AmountPerGather;
        // 기본 채집 소요 시간 (난이도)
        public float BaseGatherDuration;
        // 자원 노드 반지름 (도착 판정용)
        public float Radius;
        // 최대 매장량 (일단 무한)
        // public int MaxAmount;
    }
}