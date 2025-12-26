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
            state.RequireForUpdate<UserState>();
            // 싱글톤 엔티티 생성
            var selectionStateEntity = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponentData(selectionStateEntity, new SelectionState { Mode = SelectionMode.Idle });

            var selectionBoxEntity = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponentData(selectionBoxEntity, new SelectionBox
            {
                StartScreenPos = float2.zero,
                CurrentScreenPos = float2.zero,
                IsDragging = false
            });
        }

        public void OnUpdate(ref SystemState state)
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            var userState = SystemAPI.GetSingleton<UserState>();
            if(userState.CurrentState != UserContext.Command) return;
            
            bool leftClickPressed = mouse.leftButton.wasPressedThisFrame;
            bool leftClickReleased = mouse.leftButton.wasReleasedThisFrame;
            float2 mousePos = mouse.position.ReadValue();

            var selectionState = SystemAPI.GetSingletonRW<SelectionState>();
            var selectionBox = SystemAPI.GetSingletonRW<SelectionBox>();

            if (leftClickPressed)
            {
                // 드래그 시작
                selectionBox.ValueRW.StartScreenPos = mousePos;
                selectionBox.ValueRW.CurrentScreenPos = mousePos;
                selectionBox.ValueRW.IsDragging = true;
                selectionState.ValueRW.Mode = SelectionMode.BoxDragging;
            }
            else if (leftClickReleased && selectionBox.ValueRO.IsDragging)
            {
                float2 delta = selectionBox.ValueRO.CurrentScreenPos - selectionBox.ValueRO.StartScreenPos;
    
                // 1. 거리가 짧으면 -> 단일 클릭 모드
                if (math.length(delta) < 5f)
                {
                    selectionState.ValueRW.Mode = SelectionMode.SingleClick;
                }
                // 2. 거리가 길면(드래그면) -> 이제 볼일 다 봤으니 Idle
                else
                {
                    selectionState.ValueRW.Mode = SelectionMode.Idle;
                }

                selectionBox.ValueRW.IsDragging = false;
            }
            else if (selectionBox.ValueRO.IsDragging)
            {
                // 드래그 중 위치 갱신
                selectionBox.ValueRW.CurrentScreenPos = mousePos;
            }
        }
    }
}