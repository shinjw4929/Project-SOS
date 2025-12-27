using Unity.NetCode;

namespace Shared
{
    /// <summary>
    /// 클라이언트 → 서버: 유닛 생산 요청
    /// </summary>
    public struct ProduceUnitRequestRpc : IRpcCommand
    {
        /// <summary>생산할 건물의 Ghost ID</summary>
        public int StructureGhostId;

        /// <summary>생산할 유닛의 인덱스 (UnitCatalogElement 버퍼 내)</summary>
        public int UnitIndex;
    }
}
