using Unity.Entities;
using Unity.NetCode;

namespace Shared
{
    /// <summary>
    /// 유닛의 체력 정보 (네트워크 동기화)
    /// 서버에서 체력이 변경되면 모든 클라이언트에 자동 전송됩니다.
    /// </summary>
    [GhostComponent]
    public struct UnitHealth : IComponentData
    {
        /// <summary>현재 체력</summary>
        [GhostField] public float current;

        /// <summary>최대 체력</summary>
        [GhostField] public float max;
    }
}
