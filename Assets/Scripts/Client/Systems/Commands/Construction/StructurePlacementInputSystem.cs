using Unity.Entities;
using Unity.NetCode;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using Shared;

namespace Client
{
    [UpdateInGroup(typeof(GhostInputSystemGroup))]
    [UpdateAfter(typeof(SelectionInputSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial class StructurePlacementInputSystem : SystemBase
    {
        private Camera _mainCamera;
        private int _groundMask;

        protected override void OnCreate()
        {
            RequireForUpdate<NetworkStreamInGame>();
            RequireForUpdate<UserState>();
            RequireForUpdate<GridSettings>();
            _groundMask = 1 << LayerMask.NameToLayer("Ground");
        }

        protected override void OnUpdate()
        {
            var userState = SystemAPI.GetSingleton<UserState>();
            if (userState.CurrentState != UserContext.Construction) return;

            // 1. 카메라 캐싱 (매 프레임 FindObject 방지)
            if (_mainCamera == null) 
            {
                _mainCamera = Camera.main;
                if (_mainCamera == null) return;
            }

            var mouse = Mouse.current;
            if (mouse == null) return;

            var gridSettings = SystemAPI.GetSingleton<GridSettings>();
            float2 mousePos = mouse.position.ReadValue();
            Ray ray = _mainCamera.ScreenPointToRay(new Vector3(mousePos.x, mousePos.y, 0));

            if (UnityEngine.Physics.Raycast(ray, out RaycastHit hit, 1000f, _groundMask))
            {
                int2 gridPos = GridUtility.WorldToGrid(hit.point, gridSettings);
                
                // RefRW로 접근하여 값 수정
                ref var previewState = ref SystemAPI.GetSingletonRW<StructurePreviewState>().ValueRW;
                
                // 값이 다를 때만 쓰기 (Cache Miss 방지 미세 최적화)
                if (!previewState.GridPosition.Equals(gridPos))
                    previewState.GridPosition = gridPos;

                if (mouse.leftButton.wasPressedThisFrame && previewState.IsValidPlacement)
                {
                    // 2. ECB를 사용하여 안전하게 RPC 요청 생성
                    var ecb = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>()
                        .CreateCommandBuffer(World.Unmanaged);

                    var rpcEntity = ecb.CreateEntity();
                    ecb.AddComponent(rpcEntity, new BuildRequestRpc
                    {
                        StructureIndex = previewState.SelectedPrefabIndex,
                        GridPosition = gridPos
                    });
                    ecb.AddComponent<SendRpcCommandRequest>(rpcEntity);

                    // 상태 변경 예약
                    var userStateRw = SystemAPI.GetSingletonRW<UserState>();
                    userStateRw.ValueRW.CurrentState = UserContext.Command;
                }
            }
        }
    }
}