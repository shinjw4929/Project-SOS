using Unity.Entities;
using Unity.NetCode;
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

            GridUtility.GetBuildingSize(previewState.selectedType, out int width, out int height);

            bool canPlace = CheckGridAvailability(ref state, previewState.gridX, previewState.gridY, width, height);
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
    }
}
