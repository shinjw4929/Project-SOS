using Unity.Entities;
using Unity.NetCode;

namespace Shared
{
    /// <summary>
    /// 공간 분할 시스템 전용 그룹
    /// <para>- SpatialMapBuildSystem이 이 그룹 내에서 가장 먼저 실행</para>
    /// <para>- 타겟팅/이동 시스템은 이 그룹 이후에 실행 (UpdateAfter 사용)</para>
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial class SpatialPartitioningGroup : ComponentSystemGroup { }
}
