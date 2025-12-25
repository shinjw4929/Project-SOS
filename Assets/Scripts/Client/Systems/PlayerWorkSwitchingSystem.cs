using Unity.Entities;
using Unity.NetCode;
using UnityEngine.InputSystem;
using Shared;

namespace Client
{
    [UpdateInGroup(typeof(GhostInputSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    partial struct PlayerWorkSwitchingSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkStreamInGame>();

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

            ref var userState = ref SystemAPI.GetSingletonRW<UserState>().ValueRW;

            // Q 키로 건설 모드 토글
            if (keyboard.qKey.wasPressedThisFrame)
            {
                switch (userState.CurrentState)
                {
                    case UserContext.Command:
                        userState.CurrentState = UserContext.Construction;
                        break;
                    case UserContext.Construction:
                        userState.CurrentState = UserContext.Command;
                        break;
                    default:
                        break;
                }

                if (userState.CurrentState == UserContext.Construction)
                {
                    ref var previewState = ref SystemAPI.GetSingletonRW<BuildingPreviewState>().ValueRW;
                    previewState.selectedType = Shared.BuildingTypeEnum.Wall;
                }
            }

            // ESC 키로 건설 모드 취소
            if (keyboard.escapeKey.wasPressedThisFrame && userState.CurrentState == UserContext.Construction)
            {
                userState.CurrentState = UserContext.Command;
            }
        }

        public void OnDestroy(ref SystemState state) { }
    }
}
