using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.NetCode;
using Shared;

namespace Client
{
    /// <summary>
    /// 건물 선택 공통 유틸리티
    /// - 단축키 시스템(ConstructionMenuInputSystem)과 UI(UnitCommandUIController)에서 공유
    /// </summary>
    public static class BuildSelectionUtility
    {
        /// <summary>
        /// 로컬 인덱스 기반으로 건물을 선택하고 Construction 모드로 전환
        /// </summary>
        /// <param name="em">EntityManager</param>
        /// <param name="builderEntity">빌더 유닛 엔티티</param>
        /// <param name="localIndex">AvailableStructure 버퍼 내 로컬 인덱스 (0-3)</param>
        /// <param name="userState">UserState 싱글톤 참조</param>
        /// <param name="previewState">StructurePreviewState 싱글톤 참조</param>
        /// <param name="indexMap">Entity → 글로벌 카탈로그 인덱스 맵</param>
        /// <returns>성공 여부</returns>
        public static bool TrySelectStructure(
            EntityManager em,
            Entity builderEntity,
            int localIndex,
            ref UserState userState,
            ref StructurePreviewState previewState,
            NativeParallelHashMap<Entity, int> indexMap)
        {
            // 1. 엔티티 및 버퍼 존재 여부 확인
            if (!em.Exists(builderEntity) || !em.HasBuffer<AvailableStructure>(builderEntity))
                return false;

            var structureBuffer = em.GetBuffer<AvailableStructure>(builderEntity);

            // 2. 인덱스 범위 확인
            if (localIndex < 0 || localIndex >= structureBuffer.Length)
                return false;

            Entity targetPrefab = structureBuffer[localIndex].PrefabEntity;
            if (targetPrefab == Entity.Null)
                return false;

            // 3. 해금 여부 확인
            if (!CheckTechUnlocked(em, targetPrefab))
                return false;

            // 4. 글로벌 인덱스 조회
            if (!indexMap.TryGetValue(targetPrefab, out int globalIndex))
                return false;

            // 5. 상태 전환
            userState.CurrentState = UserContext.Construction;
            previewState.SelectedPrefab = targetPrefab;
            previewState.SelectedPrefabIndex = globalIndex;
            previewState.GridPosition = new int2(-9999, -9999);
            previewState.IsValidPlacement = false;

            return true;
        }

        /// <summary>
        /// 건물 프리팹에서 이름 추출 (태그 기반)
        /// </summary>
        public static string GetStructureName(EntityManager em, Entity prefab)
        {
            if (prefab == Entity.Null || !em.Exists(prefab))
                return "Unknown";

            if (em.HasComponent<WallTag>(prefab)) return "Wall";
            if (em.HasComponent<ResourceCenterTag>(prefab)) return "Resource Center"; // ProductionFacilityTag보다 먼저 체크
            if (em.HasComponent<ProductionFacilityTag>(prefab)) return "Barracks";
            if (em.HasComponent<TurretTag>(prefab)) return "Turret";

            return "Structure";
        }

        /// <summary>
        /// 건물 프리팹에서 비용 추출
        /// </summary>
        public static int GetStructureCost(EntityManager em, Entity prefab)
        {
            if (prefab == Entity.Null || !em.Exists(prefab))
                return 0;

            if (em.HasComponent<ProductionCost>(prefab))
                return em.GetComponentData<ProductionCost>(prefab).Cost;

            return 0;
        }

        /// <summary>
        /// 테크 트리 해금 여부 확인
        /// - Wall, ResourceCenter: 항상 해금
        /// - Barracks (ProductionFacilityTag): ResourceCenter 필요
        /// - Turret: 항상 해금 (향후 확장 가능)
        /// </summary>
        public static bool CheckTechUnlocked(EntityManager em, Entity prefab)
        {
            if (prefab == Entity.Null || !em.Exists(prefab))
                return false;

            // Wall, ResourceCenter: 항상 해금
            if (em.HasComponent<WallTag>(prefab))
                return true;
            if (em.HasComponent<ResourceCenterTag>(prefab))
                return true;

            // Barracks (ProductionFacilityTag만 있고 ResourceCenterTag 없음): ResourceCenter 필요
            if (em.HasComponent<ProductionFacilityTag>(prefab))
            {
                // 현재 유저의 UserTechState 조회 (GhostOwnerIsLocal 사용)
                using var query = em.CreateEntityQuery(
                    ComponentType.ReadOnly<UserTechState>(),
                    ComponentType.ReadOnly<UserEconomyTag>(),
                    ComponentType.ReadOnly<GhostOwnerIsLocal>()
                );

                if (query.CalculateEntityCount() != 1)
                    return false;

                var techState = query.GetSingleton<UserTechState>();
                return techState.HasResourceCenter;
            }

            // Turret: 현재는 항상 해금 (향후 확장 가능)
            if (em.HasComponent<TurretTag>(prefab))
                return true;

            return true;
        }
    }
}
