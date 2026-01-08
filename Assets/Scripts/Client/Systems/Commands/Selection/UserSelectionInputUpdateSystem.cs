using Unity.Entities;
using Unity.NetCode;
using Unity.Mathematics;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using Shared;

namespace Client
{
/// <summary>
    /// 마우스 입력 → UserSelectionInputState 업데이트
    /// - Phase 기반 상태 머신
    /// - 드래그 임계값(5px) 기준으로 클릭/드래그 구분
    /// </summary>
    [UpdateInGroup(typeof(GhostInputSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    partial struct UserSelectionInputUpdateSystem : ISystem
    {
        private const float DragThreshold = 5f;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkStreamInGame>();
            state.RequireForUpdate<UserState>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var mouse = Mouse.current;
            if (mouse == default) return;

            var userState = SystemAPI.GetSingleton<UserState>();
            if (userState.CurrentState != UserContext.Command && userState.CurrentState != UserContext.BuildMenu && userState.CurrentState != UserContext.StructureActionMenu) return;
            
            // UI 위에서 클릭 시 선택 입력 무시
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            ref var selectionState = ref SystemAPI.GetSingletonRW<UserSelectionInputState>().ValueRW;
            float2 mousePos = mouse.position.ReadValue();

            bool leftPressed = mouse.leftButton.wasPressedThisFrame;
            bool leftReleased = mouse.leftButton.wasReleasedThisFrame;

            switch (selectionState.Phase)
            {
                case SelectionPhase.Idle:
                    if (leftPressed)
                    {
                        // 마우스 누름 → Pressing 상태로 전환
                        selectionState.Phase = SelectionPhase.Pressing;
                        selectionState.StartScreenPos = mousePos;
                        selectionState.CurrentScreenPos = mousePos;
                    }
                    break;

                case SelectionPhase.Pressing:
                    if (leftReleased)
                    {
                        // 짧은 클릭 → PendingClick
                        selectionState.Phase = SelectionPhase.PendingClick;
                    }
                    else
                    {
                        // 드래그 거리 체크
                        selectionState.CurrentScreenPos = mousePos;
                        float dragDistance = math.length(mousePos - selectionState.StartScreenPos);

                        if (dragDistance >= DragThreshold)
                        {
                            // 드래그 임계값 초과 → Dragging 상태로
                            selectionState.Phase = SelectionPhase.Dragging;
                        }
                    }
                    break;

                case SelectionPhase.Dragging:
                    selectionState.CurrentScreenPos = mousePos;

                    if (leftReleased)
                    {
                        // 드래그 완료 → PendingBox
                        selectionState.Phase = SelectionPhase.PendingBox;
                    }
                    break;

                case SelectionPhase.PendingClick:
                case SelectionPhase.PendingBox:
                    // EntitySelectionSystem에서 처리 후 Idle로 복귀
                    break;
            }
        }
    }
}