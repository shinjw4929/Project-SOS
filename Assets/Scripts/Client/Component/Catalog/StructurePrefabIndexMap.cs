using Unity.Entities;
using Unity.Collections;

namespace Client
{
    /// <summary>
    /// [Managed Component] Structure 프리팹 -> 카탈로그 인덱스 매핑
    /// CatalogIndexMapInitSystem에서 1회 생성됨
    /// </summary>
    public class StructurePrefabIndexMap : IComponentData
    {
        public NativeParallelHashMap<Entity, int> Map;

        public StructurePrefabIndexMap()
        {
            Map = new NativeParallelHashMap<Entity, int>(64, Allocator.Persistent);
        }

        public void Dispose()
        {
            if (Map.IsCreated) Map.Dispose();
        }
    }
}
