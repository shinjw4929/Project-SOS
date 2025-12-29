using Unity.Entities;
using Unity.Collections;

namespace Client
{
    /// <summary>
    /// [Managed Component] Unit 프리팹 -> 카탈로그 인덱스 매핑
    /// CatalogIndexMapInitSystem에서 1회 생성됨
    /// </summary>
    public class UnitPrefabIndexMap : IComponentData
    {
        public NativeParallelHashMap<Entity, int> Map;

        public UnitPrefabIndexMap()
        {
            Map = new NativeParallelHashMap<Entity, int>(64, Allocator.Persistent);
        }

        public void Dispose()
        {
            if (Map.IsCreated) Map.Dispose();
        }
    }
}
