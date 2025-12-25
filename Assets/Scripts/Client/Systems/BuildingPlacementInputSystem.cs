using Unity.Entities;
using Unity.NetCode;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using Shared;
using Client;

namespace Client
{
    /// <summary>
    /// 건물 배치 입력을 처리하고 서버로 건설 요청 RPC를 전송
    /// - 마우스 위치로 레이캐스트하여 그리드 좌표 계산
    /// - 좌클릭 시 BuildRequestRpc 전송
    /// </summary>
    [UpdateInGroup(typeof(GhostInputSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct BuildingPlacementInputSystem : ISystem
    {
        // 레이캐스트 설정
        private const string GROUND_LAYER_NAME = "Ground";
        private const float RAYCAST_MAX_DISTANCE = 1000f;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkStreamInGame>();
            state.RequireForUpdate<UserState>();
            state.RequireForUpdate<GridSettings>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var userState = SystemAPI.GetSingleton<UserState>();
            if (userState.CurrentState != UserContext.Construction) return;

            var mouse = Mouse.current;
            if (mouse == null || Camera.main == null) return;

            var gridSettings = SystemAPI.GetSingleton<GridSettings>();
            float2 mousePos = mouse.position.ReadValue();

            Ray ray = Camera.main.ScreenPointToRay(new Vector3(mousePos.x, mousePos.y, 0));
            int groundMask = 1 << LayerMask.NameToLayer(GROUND_LAYER_NAME);

            if (UnityEngine.Physics.Raycast(ray, out RaycastHit hit, RAYCAST_MAX_DISTANCE, groundMask))
            {
                float3 worldPos = hit.point;
                int2 gridPos = GridUtility.WorldToGrid(worldPos, gridSettings);

                ref var previewState = ref SystemAPI.GetSingletonRW<BuildingPreviewState>().ValueRW;
                previewState.gridX = gridPos.x;
                previewState.gridY = gridPos.y;

                // 좌클릭 시 RPC 전송
                if (mouse.leftButton.wasPressedThisFrame && previewState.isValidPlacement)
                {
                    var rpcEntity = state.EntityManager.CreateEntity();
                    state.EntityManager.AddComponentData(rpcEntity, new BuildRequestRpc
                    {
                        buildingType = previewState.selectedType,
                        gridX = gridPos.x,
                        gridY = gridPos.y,
                        worldPosition = worldPos
                    });
                    state.EntityManager.AddComponent<SendRpcCommandRequest>(rpcEntity);

                    SystemAPI.GetSingletonRW<UserState>().ValueRW.CurrentState = UserContext.Command;
                }
            }
        }
    }
}
