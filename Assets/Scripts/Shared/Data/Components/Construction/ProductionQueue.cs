using Unity.Entities;
using Unity.NetCode;

namespace Shared
{
    [GhostComponent]
    public struct ProductionQueue : IComponentData
    {
        /// <summary>
        /// 생산 중인 유닛의 인덱스 (UnitCatalogElement 버퍼 내)
        /// Entity를 직접 저장하면 서버/클라이언트 간 ID 불일치 발생
        /// </summary>
        [GhostField] public int ProducingUnitIndex;
        [GhostField] public float Progress;
        [GhostField] public float Duration;
        [GhostField] public bool IsActive;
    }
}