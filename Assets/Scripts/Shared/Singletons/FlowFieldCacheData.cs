using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Entities;

namespace Shared
{
    /// <summary>
    /// Flow Field 캐시 데이터 (싱글톤)
    /// FlowFieldSystem이 OnCreate에서 할당, OnDestroy에서 Dispose
    /// FlowFieldSteeringSystem이 ReadOnly로 조회
    /// </summary>
    public struct FlowFieldCacheData : IComponentData
    {
        // --- Flow Field 캐시 (Flat 풀: maxFields * gridCellCount) ---
        public NativeArray<byte> SmallFieldPool;
        public NativeArray<byte> LargeFieldPool;
        public NativeHashMap<int, int> SmallKeyToPoolIndex;  // destinationKey → poolIndex
        public NativeHashMap<int, int> LargeKeyToPoolIndex;
        public int GridCellCount;

        [MarshalAs(UnmanagedType.U1)]
        public bool IsGridStale;

        // LRU 교체 추적
        public NativeArray<uint> SmallFieldLastUsedFrame;  // maxFields
        public NativeArray<uint> LargeFieldLastUsedFrame;

        // --- Passability 맵 캐시 ---
        public NativeArray<byte> SmallPassabilityMap;  // gridCellCount (패딩 0)
        public NativeArray<byte> LargePassabilityMap;  // gridCellCount (패딩 1)
    }
}
