using Unity.Entities;
using Unity.Mathematics;

namespace Client
{
    public enum CameraMode : byte
    {
        EdgePan = 0,      // 기본값: 화면 가장자리 이동
        HeroFollow = 1,   // 히어로 추적
    }

    public struct CameraState : IComponentData
    {
        public CameraMode CurrentMode;
        public Entity TargetEntity;  // HeroFollow 모드용 캐시
        public float2 ViewHalfExtent; // 카메라 뷰포트 반크기 (XZ 평면)
    }
}
