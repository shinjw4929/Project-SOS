using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

namespace Shared
{
    /// <summary>
    /// 건물 생성 공통 유틸리티 (Burst 호환 static 메서드)
    /// - HandleBuildRequestSystem, BuildArrivalSystem의 건물 엔티티 생성 로직 통합
    /// </summary>
    [BurstCompile]
    public static class BuildingUtility
    {
        /// <summary>
        /// 건물 엔티티 생성: Instantiate -> Transform -> GridPosition -> GhostOwner/Team -> UnderConstruction -> NavMesh
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Entity CreateBuilding(
            ref EntityCommandBuffer ecb,
            Entity prefab,
            float3 worldPos,
            int2 gridPos,
            int networkId,
            in ComponentLookup<LocalTransform> transformLookup,
            in ComponentLookup<ProductionInfo> productionInfoLookup,
            in ComponentLookup<NeedsNavMeshObstacle> needsNavMeshLookup)
        {
            Entity newStructure = ecb.Instantiate(prefab);

            // Transform 설정 (프리팹의 Scale/Rotation 유지)
            if (transformLookup.HasComponent(prefab))
            {
                var transform = transformLookup[prefab];
                transform.Position = worldPos;
                ecb.SetComponent(newStructure, transform);
            }
            else
            {
                ecb.SetComponent(newStructure, LocalTransform.FromPosition(worldPos));
            }

            ecb.SetComponent(newStructure, new GridPosition { Position = gridPos });
            ecb.AddComponent(newStructure, new GhostOwner { NetworkId = networkId });
            ecb.SetComponent(newStructure, new Team { teamId = networkId });

            // 건설 진행도 설정
            if (productionInfoLookup.HasComponent(prefab))
            {
                var info = productionInfoLookup[prefab];
                ecb.AddComponent(newStructure, new UnderConstructionTag
                {
                    Progress = 0f,
                    TotalBuildTime = info.ProductionTime
                });
            }

            // NavMesh 장애물 활성화
            if (needsNavMeshLookup.HasComponent(prefab))
            {
                ecb.SetComponentEnabled<NeedsNavMeshObstacle>(newStructure, true);
            }

            return newStructure;
        }
    }
}
