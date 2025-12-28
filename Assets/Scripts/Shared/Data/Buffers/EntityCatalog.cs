using Unity.Entities;

namespace Shared
{
    // [싱글톤 태그] 이 시스템의 존재를 알리는 식별자
    public struct StructureCatalog : IComponentData { }
    public struct UnitCatalog : IComponentData { }
    
    // [버퍼 요소] 실제 프리팹 데이터를 담는 버퍼
    public struct UnitCatalogElement : IBufferElementData
    {
        public Entity PrefabEntity;
    }

    public struct StructureCatalogElement : IBufferElementData
    {
        public Entity PrefabEntity;
    }
    
    
    // [부분 집합] 유닛이 건설, 생산 가능한 프리팹 데이터
    public struct AvailableUnit : IBufferElementData
    {
        public Entity PrefabEntity;
    }
    
    public struct AvailableStructure : IBufferElementData
    {
        public Entity PrefabEntity;
    }
}
