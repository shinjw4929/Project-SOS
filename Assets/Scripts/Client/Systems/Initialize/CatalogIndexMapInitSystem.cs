using Unity.Entities;
using Shared;

namespace Client
{
    /// <summary>
    /// [Client 전용] 카탈로그 인덱스 맵 싱글톤 초기화
    /// StructureCatalog, UnitCatalog가 존재할 때 1회 실행
    /// </summary>
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateAfter(typeof(ClientBootstrapSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial class CatalogIndexMapInitSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<StructureCatalog>();
            RequireForUpdate<UnitCatalog>();
        }

        protected override void OnUpdate()
        {
            // 1회만 실행 후 비활성화
            Enabled = false;

            // Structure Index Map 생성
            if (!SystemAPI.ManagedAPI.HasSingleton<StructurePrefabIndexMap>())
            {
                CreateStructureIndexMap();
            }

            // Unit Index Map 생성
            if (!SystemAPI.ManagedAPI.HasSingleton<UnitPrefabIndexMap>())
            {
                CreateUnitIndexMap();
            }
        }

        protected override void OnDestroy()
        {
            // Managed Component 정리
            if (SystemAPI.ManagedAPI.HasSingleton<StructurePrefabIndexMap>())
            {
                var structureMap = SystemAPI.ManagedAPI.GetSingleton<StructurePrefabIndexMap>();
                structureMap.Dispose();
            }

            if (SystemAPI.ManagedAPI.HasSingleton<UnitPrefabIndexMap>())
            {
                var unitMap = SystemAPI.ManagedAPI.GetSingleton<UnitPrefabIndexMap>();
                unitMap.Dispose();
            }
        }

        private void CreateStructureIndexMap()
        {
            var entity = EntityManager.CreateEntity();
            EntityManager.SetName(entity, "Singleton_StructurePrefabIndexMap");

            var indexMap = new StructurePrefabIndexMap();

            // 카탈로그에서 인덱스 맵 빌드
            var catalogEntity = SystemAPI.GetSingletonEntity<StructureCatalog>();
            var buffer = SystemAPI.GetBuffer<StructureCatalogElement>(catalogEntity);

            for (int i = 0; i < buffer.Length; i++)
            {
                if (buffer[i].PrefabEntity != Entity.Null)
                {
                    indexMap.Map.TryAdd(buffer[i].PrefabEntity, i);
                }
            }

            EntityManager.AddComponentData(entity, indexMap);
        }

        private void CreateUnitIndexMap()
        {
            var entity = EntityManager.CreateEntity();
            EntityManager.SetName(entity, "Singleton_UnitPrefabIndexMap");

            var indexMap = new UnitPrefabIndexMap();

            // 카탈로그에서 인덱스 맵 빌드
            var catalogEntity = SystemAPI.GetSingletonEntity<UnitCatalog>();
            var buffer = SystemAPI.GetBuffer<UnitCatalogElement>(catalogEntity);

            for (int i = 0; i < buffer.Length; i++)
            {
                if (buffer[i].PrefabEntity != Entity.Null)
                {
                    indexMap.Map.TryAdd(buffer[i].PrefabEntity, i);
                }
            }

            EntityManager.AddComponentData(entity, indexMap);
        }
    }
}
