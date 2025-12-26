using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Shared;

namespace Client
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct StructurePreviewUpdateSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<UserState>();
            state.RequireForUpdate<StructurePreviewState>();
            state.RequireForUpdate<GridSettings>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var userState = SystemAPI.GetSingleton<UserState>();
            ref var previewState = ref SystemAPI.GetSingletonRW<StructurePreviewState>().ValueRW;

            // 건설 모드가 아니거나, 선택된 프리팹이 없으면 무효
            if (userState.CurrentState != UserContext.Construction || previewState.SelectedPrefab == Entity.Null)
            {
                previewState.IsValidPlacement = false;
                return;
            }

            // 프리팹 유효성 및 크기 컴포넌트 확인
            Entity prefab = previewState.SelectedPrefab;
            if (!state.EntityManager.Exists(prefab) || !state.EntityManager.HasComponent<StructureFootprint>(prefab))
            {
                previewState.IsValidPlacement = false;
                return;
            }

            // [변경] Metadata 대신 Footprint 사용
            var footprint = state.EntityManager.GetComponentData<StructureFootprint>(prefab);
            int width = footprint.Width;
            int length = footprint.Length;

            // 그리드 설정
            var gridSettings = SystemAPI.GetSingleton<GridSettings>();
            var gridEntity = SystemAPI.GetSingletonEntity<GridSettings>();

            // 점유 확인
            bool isOccupied = false;
            if (SystemAPI.HasBuffer<GridCell>(gridEntity))
            {
                var buffer = SystemAPI.GetBuffer<GridCell>(gridEntity);
                // previewState.GridPosition.x, y (int2) 사용
                isOccupied = GridUtility.IsOccupied(buffer, previewState.GridPosition.x, previewState.GridPosition.y,
                    width, length, gridSettings.GridSize.x, gridSettings.GridSize.y);
            }

            // 유닛 충돌 확인
            bool hasUnitCollision = CheckUnitCollision(ref state, previewState.GridPosition.x, previewState.GridPosition.y,
                width, length, gridSettings);

            previewState.IsValidPlacement = !isOccupied && !hasUnitCollision;
        }

        private bool CheckUnitCollision(ref SystemState state, int gridX, int gridY, int width, int length, GridSettings gridSettings)
        {
            // (충돌 로직은 기존과 동일)
            float3 buildingCenter = GridUtility.GridToWorld(gridX, gridY, width, length, gridSettings);
            float halfWidth = width * gridSettings.CellSize / 2f;
            float halfLength = length * gridSettings.CellSize / 2f;

            foreach (var (transform, _) in SystemAPI.Query<RefRO<LocalTransform>, RefRO<UnitTag>>()) // UnitTag로 변경
            {
                float3 unitPos = transform.ValueRO.Position;
                if (unitPos.x >= buildingCenter.x - halfWidth && unitPos.x <= buildingCenter.x + halfWidth &&
                    unitPos.z >= buildingCenter.z - halfLength && unitPos.z <= buildingCenter.z + halfLength)
                {
                    return true;
                }
            }
            return false;
        }
    }
}