using Unity.Entities;
using Unity.Mathematics;

namespace Server
{
    /// <summary>
    /// 건설 이동 시 FlowField 조기 정지 + 도착 판정 기준.
    /// AABB 표면에서 Value 이내 진입 시 웨이포인트 생성 중단.
    /// HandleBuildMoveRequestSystem이 추가, BuildArrivalSystem이 제거.
    /// </summary>
    public struct BuildApproachRadius : IComponentData
    {
        public float Value;
        public float3 Center;
        public float HalfW;
        public float HalfL;
    }
}
