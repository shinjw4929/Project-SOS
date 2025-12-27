using Unity.Entities;

namespace Shared
{
    /// <summary>
    /// 배럭이 생산할 수 있는 유닛 프리팹 목록
    /// BarracksAuthoring에서 설정
    /// </summary>
    public struct ProducibleUnitElement : IBufferElementData
    {
        /// <summary>생산할 유닛의 프리팹 엔티티</summary>
        public Entity PrefabEntity;

        /// <summary>EntitiesReferences 버퍼 내 인덱스 (RPC 전송용)</summary>
        public int PrefabIndex;
    }
}
