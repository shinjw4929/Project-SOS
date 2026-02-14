using Unity.Entities;
using Unity.Mathematics;

namespace Shared
{
    /// <summary>
    /// Connection 엔티티에 부착. 클라이언트 카메라 뷰포트 반크기 (CameraPositionRpc로 수신).
    /// GhostRelevancySystem에서 AABB 기반 Relevancy 판정에 사용.
    /// </summary>
    public struct ConnectionViewExtent : IComponentData
    {
        public float2 HalfExtent; // (halfX, halfZ) 뷰포트 지면 투영 반크기
    }
}
