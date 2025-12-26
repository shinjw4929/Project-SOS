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
    public partial struct RTSCommandInputSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkStreamInGame>();
            state.RequireForUpdate<NetworkId>();
        }

        public void OnUpdate(ref SystemState state)
        {
            // ------------------------------------------------------------
            // 1. 마우스 입력 처리 (클릭 -> InputState 갱신)
            // ------------------------------------------------------------
            if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame && Camera.main != null)
            {
                float2 mousePos = Mouse.current.position.ReadValue();
                UnityEngine.Ray ray = Camera.main.ScreenPointToRay(new Vector3(mousePos.x, mousePos.y, 0));
                
                // Ground 레이어만 체크
                int groundMask = 1 << LayerMask.NameToLayer("Ground");

                if (UnityEngine.Physics.Raycast(ray, out UnityEngine.RaycastHit hit, 1000f, groundMask))
                {
                    float3 newTargetPos = hit.point;

                    // 선택된 '내 유닛'들의 InputState를 즉시 갱신
                    // (서버 데이터인 MoveTarget은 건드리지 않음 -> 롤백 방지)
                    foreach (var (inputState, entity) in SystemAPI.Query<RefRW<RTSInputState>>()
                        .WithAll<Selected, GhostOwnerIsLocal>() // 선택되고 + 내 것인 유닛
                        .WithEntityAccess())
                    {
                        inputState.ValueRW.TargetPosition = newTargetPos;
                        inputState.ValueRW.HasTarget = true;
                    }
                }
            }

            // ------------------------------------------------------------
            // 2. 명령 생성 (InputState -> RTSCommand)
            // ------------------------------------------------------------
            var networkTime = SystemAPI.GetSingleton<NetworkTime>();
            NetworkTick tick = networkTime.ServerTick;

            // 모든 내 유닛에 대해, 현재 InputState를 명령서로 작성하여 제출
            foreach (var (inputState, inputBuffer) in SystemAPI.Query<RefRO<RTSInputState>, DynamicBuffer<RTSCommand>>()
                .WithAll<GhostOwnerIsLocal>())
            {
                var command = new RTSCommand
                {
                    Tick = tick,
                    TargetPosition = inputState.ValueRO.TargetPosition,
                    // hasTarget이 true면 Move 명령, 아니면 None
                    CommandType = inputState.ValueRO.HasTarget ? RTSCommandType.Move : RTSCommandType.None
                };

                // 이전 틱의 명령과 다르더라도 무조건 현재 의도(InputState)를 보냄 -> 즉각 반응
                inputBuffer.AddCommandData(command);
            }
        }
    }
}