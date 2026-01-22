using Unity.Entities;
using Unity.Collections;
using Unity.NetCode;
using Shared;

namespace Client
{
    /// <summary>
    /// 유닛 생산 공통 유틸리티
    /// - 키보드 시스템(StructureCommandInputSystem)과 UI(StructureCommandUIController)에서 공유
    /// </summary>
    public static class ProduceSelectionUtility
    {
        /// <summary>
        /// 로컬 인덱스 기반으로 유닛 생산 RPC 전송
        /// </summary>
        /// <param name="em">EntityManager</param>
        /// <param name="producerEntity">생산 시설 엔티티</param>
        /// <param name="localIndex">AvailableUnit 버퍼 내 로컬 인덱스 (0-3)</param>
        /// <param name="unitIndexMap">Entity → 글로벌 카탈로그 인덱스 맵</param>
        /// <returns>성공 여부</returns>
        public static bool TryProduceUnit(
            EntityManager em,
            Entity producerEntity,
            int localIndex,
            NativeParallelHashMap<Entity, int> unitIndexMap)
        {
            // 1. 엔티티 및 버퍼 존재 여부 확인
            if (!em.Exists(producerEntity) || !em.HasBuffer<AvailableUnit>(producerEntity))
                return false;

            // 2. ProductionFacilityTag 확인
            if (!em.HasComponent<ProductionFacilityTag>(producerEntity))
                return false;

            var unitBuffer = em.GetBuffer<AvailableUnit>(producerEntity);

            // 3. 인덱스 범위 확인
            if (localIndex < 0 || localIndex >= unitBuffer.Length)
                return false;

            Entity targetPrefab = unitBuffer[localIndex].PrefabEntity;
            if (targetPrefab == Entity.Null)
                return false;

            // 4. 글로벌 인덱스 조회
            if (!unitIndexMap.TryGetValue(targetPrefab, out int globalIndex))
                return false;

            // 5. GhostInstance 확인 및 RPC 전송
            if (!em.HasComponent<GhostInstance>(producerEntity))
                return false;

            var ghost = em.GetComponentData<GhostInstance>(producerEntity);
            var rpcEntity = em.CreateEntity();
            em.AddComponentData(rpcEntity, new ProduceUnitRequestRpc
            {
                StructureGhostId = ghost.ghostId,
                UnitIndex = globalIndex
            });
            em.AddComponent<SendRpcCommandRequest>(rpcEntity);

            return true;
        }

        /// <summary>
        /// 유닛 프리팹에서 이름 추출 (태그 기반)
        /// </summary>
        public static string GetUnitName(EntityManager em, Entity prefab)
        {
            if (prefab == Entity.Null || !em.Exists(prefab))
                return "Unknown";

            // 영웅/특수 유닛
            if (em.HasComponent<HeroTag>(prefab)) return "Hero";
            if (em.HasComponent<WorkerTag>(prefab)) return "Worker";

            // 병사 유닛 (세부 태그 우선 체크)
            if (em.HasComponent<StrikerTag>(prefab)) return "Striker";
            if (em.HasComponent<ArcherTag>(prefab)) return "Archer";
            if (em.HasComponent<TankTag>(prefab)) return "Tank";

            // 일반 병사 (폴백)
            if (em.HasComponent<SoldierTag>(prefab)) return "Soldier";

            return "Unit";
        }

        /// <summary>
        /// 유닛 프리팹에서 비용 추출
        /// </summary>
        public static int GetUnitCost(EntityManager em, Entity prefab)
        {
            if (prefab == Entity.Null || !em.Exists(prefab))
                return 0;

            if (em.HasComponent<ProductionCost>(prefab))
                return em.GetComponentData<ProductionCost>(prefab).Cost;

            return 0;
        }

        /// <summary>
        /// 유닛 프리팹에서 생산 시간 추출
        /// </summary>
        public static float GetUnitProductionTime(EntityManager em, Entity prefab)
        {
            if (prefab == Entity.Null || !em.Exists(prefab))
                return 0f;

            if (em.HasComponent<ProductionInfo>(prefab))
                return em.GetComponentData<ProductionInfo>(prefab).ProductionTime;

            return 0f;
        }

        /// <summary>
        /// 테크 트리 해금 여부 확인 (현재는 항상 true, 추후 확장)
        /// </summary>
        public static bool CheckUnitUnlocked(EntityManager em, Entity prefab)
        {
            // TODO: TechRequirement 컴포넌트 기반 해금 체크
            // 현재는 모든 유닛이 해금된 것으로 처리
            return true;
        }
    }
}
