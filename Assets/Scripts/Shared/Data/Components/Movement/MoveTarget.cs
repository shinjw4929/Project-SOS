using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace Shared
{
    [GhostComponent]
    public struct MoveTarget : IComponentData
    {
        [GhostField] public float3 position;
        [GhostField] public bool isValid;
        
        // 다음 웨이포인트 정보 (부드러운 코너링용)
        [GhostField] public float3 NextPosition;
        [GhostField] public bool HasNextPosition;
    }
}