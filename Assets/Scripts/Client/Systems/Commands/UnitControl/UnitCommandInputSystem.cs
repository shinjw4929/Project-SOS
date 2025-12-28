using Unity.Entities;
using Unity.NetCode;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using Shared;

namespace Client
{
    /// <summary>
    /// 사용자가 유닛에게 명령을 입력하는 시스템
    /// - 우클릭 → 이동 명령
    /// - (향후) A-클릭 → 공격 명령, S → 정지 명령 등
    /// </summary>
    [UpdateInGroup(typeof(GhostInputSystemGroup))]
    [UpdateAfter(typeof(SelectionStateSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct UnitCommandInputSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkStreamInGame>();
            state.RequireForUpdate<NetworkId>();
            state.RequireForUpdate<UserState>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var userState = SystemAPI.GetSingleton<UserState>();
            if (userState.CurrentState == UserContext.Dead) return;

            ProcessRightClickCommand(ref state);
            SubmitCommands(ref state);
        }

        /// <summary>
        /// 우클릭 입력 처리 → RTSInputState 갱신
        /// </summary>
        private void ProcessRightClickCommand(ref SystemState state)
        {
            var mouse = Mouse.current;
            if (mouse == null || !mouse.rightButton.wasPressedThisFrame) return;
            if (Camera.main == null) return;

            float2 mousePos = mouse.position.ReadValue();
            Ray ray = Camera.main.ScreenPointToRay(new Vector3(mousePos.x, mousePos.y, 0));

            int groundMask = 1 << LayerMask.NameToLayer("Ground");

            if (Physics.Raycast(ray, out RaycastHit hit, 1000f, groundMask))
            {
                float3 targetPos = hit.point;

                // 선택된 내 유닛들의 InputState 갱신
                foreach (var inputState in SystemAPI.Query<RefRW<RTSInputState>>()
                    .WithAll<Selected, GhostOwnerIsLocal>())
                {
                    inputState.ValueRW.TargetPosition = targetPos;
                    inputState.ValueRW.HasTarget = true;
                }
            }
        }

        /// <summary>
        /// RTSInputState → RTSCommand 버퍼에 명령 제출
        /// </summary>
        private void SubmitCommands(ref SystemState state)
        {
            var networkTime = SystemAPI.GetSingleton<NetworkTime>();
            NetworkTick tick = networkTime.ServerTick;

            foreach (var (inputState, inputBuffer) in SystemAPI.Query<RefRO<RTSInputState>, DynamicBuffer<RTSCommand>>()
                .WithAll<GhostOwnerIsLocal>())
            {
                var command = new RTSCommand
                {
                    Tick = tick,
                    TargetPosition = inputState.ValueRO.TargetPosition,
                    TargetGhostId = 0, // 향후 공격 명령에서 사용
                    CommandType = inputState.ValueRO.HasTarget ? RTSCommandType.Move : RTSCommandType.None
                };

                inputBuffer.AddCommandData(command);
            }
        }
    }
}