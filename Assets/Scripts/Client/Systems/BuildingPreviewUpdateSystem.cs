using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using Shared;
using Client;

namespace Client
{
    /// <summary>
    /// 건물 배치 프리뷰의 유효성 검사
    /// - 현재 그리드 위치에 건물 배치가 가능한지 검증
    /// - 기존 건물과의 충돌 체크
    /// - BuildingPreviewState.isValidPlacement 업데이트
    /// </summary>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct BuildingPreviewUpdateSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<UserState>();
            state.RequireForUpdate<BuildingPreviewState>();
            state.RequireForUpdate<GridSettings>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var userState = SystemAPI.GetSingleton<UserState>();
            ref var previewState = ref SystemAPI.GetSingletonRW<BuildingPreviewState>().ValueRW;

            if (userState.CurrentState != UserContext.Construction)
            {
                previewState.isValidPlacement = false;
                return;
            }

            var gridSettings = SystemAPI.GetSingleton<GridSettings>();
            GridUtility.GetBuildingSize(previewState.selectedType, out int width, out int height);

            bool canPlace = CheckGridAvailability(ref state, previewState.gridX, previewState.gridY, width, height);
            if (canPlace)
            {
                canPlace = !CheckUnitCollision(ref state, previewState.gridX, previewState.gridY, width, height, gridSettings);
            }
            previewState.isValidPlacement = canPlace;
        }

        /// <summary>
        /// 그리드 위치에 건물 배치가 가능한지 검사 (기존 건물과의 충돌 체크)
        /// </summary>
        private bool CheckGridAvailability(ref SystemState state, int gridX, int gridY, int width, int height)
        {
            foreach (var occupancy in SystemAPI.Query<RefRO<GridOccupancy>>())
            {
                if (GridUtility.IsOverlapping(gridX, gridY, width, height,
                                              occupancy.ValueRO.gridX, occupancy.ValueRO.gridY,
                                              occupancy.ValueRO.width, occupancy.ValueRO.height))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 유닛과의 충돌 검사 (건물 배치 영역 내에 유닛이 있는지 확인)
        /// </summary>
        private bool CheckUnitCollision(ref SystemState state, int gridX, int gridY, int width, int height, GridSettings gridSettings)
        {
            float3 buildingCenter = GridUtility.GridToWorld(gridX, gridY, width, height, gridSettings);
            float halfWidth = width * gridSettings.cellSize / 2f;
            float halfHeight = height * gridSettings.cellSize / 2f;

            foreach (var (transform, _) in SystemAPI.Query<RefRO<LocalTransform>, RefRO<UnitType>>())
            {
                float3 unitPos = transform.ValueRO.Position;

                // AABB 충돌 검사 (XZ 평면)
                if (unitPos.x >= buildingCenter.x - halfWidth && unitPos.x <= buildingCenter.x + halfWidth &&
                    unitPos.z >= buildingCenter.z - halfHeight && unitPos.z <= buildingCenter.z + halfHeight)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
