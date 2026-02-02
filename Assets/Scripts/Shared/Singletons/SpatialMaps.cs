using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Shared
{
    /// <summary>
    /// 타겟팅용 공간 분할 맵 엔트리
    /// <para>- 적→아군, 유닛→적 타겟팅에 사용</para>
    /// </summary>
    public struct SpatialTargetEntry
    {
        public Entity Entity;
        public float3 Position;
        public int TeamId;
    }

    /// <summary>
    /// 이동/충돌 회피용 공간 분할 맵 엔트리
    /// <para>- KinematicJob에서 Lookup으로 실시간 상태 확인</para>
    /// </summary>
    public struct SpatialMovementEntry
    {
        public Entity Entity;
        // Flags 제거: KinematicJob에서 Lookup으로 실시간 확인
    }

    /// <summary>
    /// 공유 공간 분할 맵 싱글톤
    /// <para>- SpatialMapBuildSystem에서 Persistent 할당 후 매 프레임 Clear + 재빌드</para>
    /// <para>- Job dependency chain으로 동기화 (CompleteDependency 불필요)</para>
    /// </summary>
    public struct SpatialMaps : IComponentData
    {
        /// <summary>타겟팅용 맵 (셀 크기: 10.0f)</summary>
        public NativeParallelMultiHashMap<int, SpatialTargetEntry> TargetingMap;

        /// <summary>이동/충돌 회피용 맵 (셀 크기: 3.0f)</summary>
        public NativeParallelMultiHashMap<int, SpatialMovementEntry> MovementMap;

        /// <summary>맵 유효성 플래그 (빌드 완료 여부)</summary>
        public bool IsValid;
    }
}
