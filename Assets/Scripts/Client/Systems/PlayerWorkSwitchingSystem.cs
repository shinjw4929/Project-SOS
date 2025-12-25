using Unity.Entities;
using Unity.NetCode;
using UnityEngine.InputSystem;

namespace Client
{
    [UpdateInGroup(typeof(GhostInputSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    partial struct PlayerWorkSwitchingSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkStreamInGame>();

            // PlayerBuildState 싱글톤 (isBuildMode 초기값 false로 변경)
            var buildStateEntity = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponentData(buildStateEntity, new PlayerBuildState { isBuildMode = false });

            // BuildingPreviewState 싱글톤 추가
            var previewStateEntity = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponentData(previewStateEntity, new BuildingPreviewState
            {
                selectedType = Shared.BuildingTypeEnum.Wall,
                gridX = 0,
                gridY = 0,
                isValidPlacement = false
            });
        }

        public void OnUpdate(ref SystemState state)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            ref var buildState = ref SystemAPI.GetSingletonRW<PlayerBuildState>().ValueRW;

            // B 키로 건설 모드 토글
            if (keyboard.bKey.wasPressedThisFrame)
            {
                buildState.isBuildMode = !buildState.isBuildMode;

                if (buildState.isBuildMode)
                {
                    ref var previewState = ref SystemAPI.GetSingletonRW<BuildingPreviewState>().ValueRW;
                    previewState.selectedType = Shared.BuildingTypeEnum.Wall;
                }
            }

            // ESC 키로 건설 모드 취소
            if (keyboard.escapeKey.wasPressedThisFrame && buildState.isBuildMode)
            {
                buildState.isBuildMode = false;
            }
        }

        public void OnDestroy(ref SystemState state) { }
    }
}
