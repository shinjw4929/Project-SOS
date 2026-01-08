using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using Shared;

namespace Client
{
    /// <summary>
    /// 사용자의 마우스/키보드 입력을 UnitCommand로 변환하여 전송하는 시스템
    /// </summary>
    [UpdateInGroup(typeof(GhostInputSystemGroup))]
    [UpdateAfter(typeof(SelectedEntityInfoUpdateSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct UnitCommandInputSystem : ISystem
    {
        private ComponentLookup<PendingBuildRequest> _pendingBuildLookup;
        private ComponentLookup<GhostInstance> _ghostInstanceLookup;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkStreamInGame>();
            state.RequireForUpdate<NetworkId>();
            state.RequireForUpdate<UserState>();
            state.RequireForUpdate<BeginSimulationEntityCommandBufferSystem.Singleton>();

            _pendingBuildLookup = state.GetComponentLookup<PendingBuildRequest>(true);
            _ghostInstanceLookup = state.GetComponentLookup<GhostInstance>(true);
        }

        public void OnUpdate(ref SystemState state)
        {
            var userState = SystemAPI.GetSingleton<UserState>();
            if (userState.CurrentState == UserContext.Dead) return;

            // Construction 모드에서는 입력 처리하지 않음
            // (StructurePlacementInputSystem에서 BuildKey 명령을 처리)
            if (userState.CurrentState == UserContext.Construction) return;

            // Lookup 업데이트
            _pendingBuildLookup.Update(ref state);
            _ghostInstanceLookup.Update(ref state);

            ProcessMouseInput(ref state);
        }

        private void ProcessMouseInput(ref SystemState state)
        {
            var mouse = Mouse.current;
            if (mouse == default) return;
            if (!Camera.main) return;

            // 우클릭했을 때만 명령 전송
            if (mouse.rightButton.wasPressedThisFrame)
            {
                // 1. Raycast 수행
                float2 mousePos = mouse.position.ReadValue();
                UnityEngine.Ray ray = Camera.main.ScreenPointToRay(new Vector3(mousePos.x, mousePos.y, 0));

                // 레이어 마스크 (Ground, Unit, Resource 등)
                int layerMask = (1 << 3) | (1 << 6) | (1 << 7); // 예시: 3=Ground, 6=Resource, 7=Structure

                if (Physics.Raycast(ray, out UnityEngine.RaycastHit hit, 1000f, layerMask))
                {
                    // 2. 클릭된 대상 분석 (Entity 찾기)
                    Entity targetEntity = Entity.Null;
                    int targetGhostId = 0;

                    // [간소화] 일단은 땅 클릭(이동)만 있다고 가정하고, 타겟 ID는 0으로 둠.
                    // 나중에 '적 유닛 클릭' 등을 구현할 때 여기서 targetGhostId를 채우면 됨.

                    // 3. 명령 전송
                    SendCommandToSelectedUnits(ref state, hit.point, targetGhostId);
                    return; // 명령 전송했으면 빈 명령 전송 스킵
                }
            }

            // 입력이 없을 때는 빈 명령(None) 전송하여 이전 명령 반복 방지
            SendEmptyCommandToAllUnits(ref state);
        }

        private void SendCommandToSelectedUnits(ref SystemState state, float3 goalPos, int targetGhostId)
        {
            var networkTime = SystemAPI.GetSingleton<NetworkTime>();
            var tick = networkTime.ServerTick;

            var ecb = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged);

            int selectedCount = 0;

            // 선택된 내 유닛들에게 명령 하달
            foreach (var (inputBuffer, entity) in SystemAPI.Query<DynamicBuffer<UnitCommand>>()
                .WithAll<Selected, GhostOwnerIsLocal>() // 내가 소유하고 선택한 유닛만
                .WithEntityAccess())
            {
                selectedCount++;

                // 1. UnitCommand 생성
                var command = new UnitCommand
                {
                    Tick = tick,
                    CommandType = UnitCommandType.RightClick, // 우클릭 통합 명령
                    GoalPosition = goalPos,
                    TargetGhostId = targetGhostId,
                };

                // 2. 버퍼에 추가 (Netcode가 서버로 전송함)
                inputBuffer.AddCommandData(command);

                // 3. 건설 대기 상태였다면 취소 (이동 명령이 우선이므로)
                if (_pendingBuildLookup.HasComponent(entity))
                {
                    ecb.RemoveComponent<PendingBuildRequest>(entity);
                }
            }

            // if (selectedCount == 0)
            // {
            //     UnityEngine.Debug.LogWarning("[CLIENT] 우클릭했지만 선택된 유닛이 없음!");
            // }
        }

        /// <summary>
        /// 빈 명령(None)을 모든 유닛에 전송하여 이전 명령 반복 방지
        /// </summary>
        private void SendEmptyCommandToAllUnits(ref SystemState state)
        {
            var networkTime = SystemAPI.GetSingleton<NetworkTime>();
            var tick = networkTime.ServerTick;

            // 내 유닛들에게 빈 명령 전송 (이전 명령 반복 방지)
            // PendingBuildRequest가 있는 유닛은 제외 (BuildKey 명령 보호)
            foreach (var inputBuffer in SystemAPI.Query<DynamicBuffer<UnitCommand>>()
                .WithAll<GhostOwnerIsLocal>()
                .WithNone<PendingBuildRequest>())
            {
                var emptyCommand = new UnitCommand
                {
                    Tick = tick,
                    CommandType = UnitCommandType.None,
                    GoalPosition = default,
                    TargetGhostId = 0,
                };

                inputBuffer.AddCommandData(emptyCommand);
            }
        }
    }
}