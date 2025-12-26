using Unity.Entities;
using Unity.NetCode;

namespace Shared
{
    // 건물이 차지하는 그리드 칸 수
    [GhostComponent(PrefabType = GhostPrefabType.All)]
    public struct StructureFootprint : IComponentData
    {
        [GhostField] public int Width;  // 가로 길이
        [GhostField] public int Length; // 세로 길이
        [GhostField] public float Height; // 건물 높이
        // 센터 포지션 계산을 위한 오프셋을 미리 계산해 둘 수도 있음
    }
}