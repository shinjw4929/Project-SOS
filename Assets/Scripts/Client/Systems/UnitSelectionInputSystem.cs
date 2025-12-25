using Unity.Entities;
using Unity.NetCode;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using Shared;

namespace Client
{
    [UpdateInGroup(typeof(GhostInputSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    partial struct UnitSelectionInputSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkStreamInGame>();

            // 싱글톤 엔티티 생성
            var selectionStateEntity = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponentData(selectionStateEntity, new SelectionState { mode = SelectionMode.Idle });

            var selectionBoxEntity = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponentData(selectionBoxEntity, new SelectionBox
            {
                startScreenPos = float2.zero,
                currentScreenPos = float2.zero,
                isDragging = false
            });
        }

        public void OnUpdate(ref SystemState state)
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            bool leftClickPressed = mouse.leftButton.wasPressedThisFrame;
            bool leftClickReleased = mouse.leftButton.wasReleasedThisFrame;
            float2 mousePos = mouse.position.ReadValue();

            var selectionState = SystemAPI.GetSingletonRW<SelectionState>();
            var selectionBox = SystemAPI.GetSingletonRW<SelectionBox>();

            if (leftClickPressed)
            {
                // 드래그 시작
                selectionBox.ValueRW.startScreenPos = mousePos;
                selectionBox.ValueRW.currentScreenPos = mousePos;
                selectionBox.ValueRW.isDragging = true;
                selectionState.ValueRW.mode = SelectionMode.BoxDragging;
            }
            else if (leftClickReleased && selectionBox.ValueRO.isDragging)
            {
                float2 delta = selectionBox.ValueRO.currentScreenPos - selectionBox.ValueRO.startScreenPos;
    
                // 1. 거리가 짧으면 -> 단일 클릭 모드
                if (math.length(delta) < 5f)
                {
                    selectionState.ValueRW.mode = SelectionMode.SingleClick;
                }
                // 2. [핵심 추가] 거리가 길면(드래그면) -> 이제 볼일 다 봤으니 Idle로 복귀!
                else
                {
                    selectionState.ValueRW.mode = SelectionMode.Idle;
                }

                selectionBox.ValueRW.isDragging = false;
            }
            else if (selectionBox.ValueRO.isDragging)
            {
                // 드래그 중 위치 갱신
                selectionBox.ValueRW.currentScreenPos = mousePos;
            }
        }
    }
}