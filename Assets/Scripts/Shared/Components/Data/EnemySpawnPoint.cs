using Unity.Entities;
using Unity.Mathematics;

namespace Shared
{
    /// <summary>
    /// 적 스폰 위치 마커 컴포넌트.
    /// 씬에 배치된 스폰 포인트의 위치를 저장.
    /// </summary>
    public struct EnemySpawnPoint : IComponentData
    {
        /// <summary>
        /// 스폰 위치 (월드 좌표)
        /// </summary>
        public float3 Position;
    }
}
