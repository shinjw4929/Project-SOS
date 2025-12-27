using Unity.Entities;
using Unity.NetCode;
using UnityEngine.InputSystem;
using Shared;

namespace Client
{
    /// <summary>
    /// 건물 명령 입력 처리
    /// - Command + 건물 선택 + Q키 → StructureMenu 상태
    /// - StructureMenu + ESC → Command 복귀
    /// - StructureMenu + WallTag + R → 자폭 RPC
    /// - StructureMenu + BarracksTag + Q/W → 생산 RPC
    /// </summary>
    [UpdateInGroup(typeof(GhostInputSystemGroup))]
    [UpdateAfter(typeof(SelectionStateSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct StructureCommandInputSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkStreamInGame>();
            state.RequireForUpdate<CurrentSelection>();
            state.RequireForUpdate<UserState>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            var selection = SystemAPI.GetSingleton<CurrentSelection>();
            var userStateEntity = SystemAPI.GetSingletonEntity<UserState>();
            var userState = SystemAPI.GetSingleton<UserState>();

            // Command 상태에서 건물 선택 + Q키 → StructureMenu
            if (userState.CurrentState == UserContext.Command)
            {
                if (keyboard.qKey.wasPressedThisFrame)
                {
                    if (selection.Category == SelectionCategory.Structure &&
                        selection.IsOwnedSelection &&
                        selection.PrimaryEntity != Entity.Null)
                    {
                        // StructureMenu 상태로 전환
                        state.EntityManager.SetComponentData(userStateEntity, new UserState
                        {
                            CurrentState = UserContext.StructureMenu
                        });
                        return;
                    }
                }
                return;
            }

            // StructureMenu 상태
            if (userState.CurrentState == UserContext.StructureMenu)
            {
                // ESC → Command 복귀
                if (keyboard.escapeKey.wasPressedThisFrame)
                {
                    state.EntityManager.SetComponentData(userStateEntity, new UserState
                    {
                        CurrentState = UserContext.Command
                    });
                    return;
                }

                // 선택이 해제되었거나 건물이 아니면 Command 복귀
                if (selection.Category != SelectionCategory.Structure ||
                    !selection.IsOwnedSelection ||
                    selection.PrimaryEntity == Entity.Null)
                {
                    state.EntityManager.SetComponentData(userStateEntity, new UserState
                    {
                        CurrentState = UserContext.Command
                    });
                    return;
                }

                var primaryEntity = selection.PrimaryEntity;

                // 벽 자폭 (R키)
                if (keyboard.rKey.wasPressedThisFrame)
                {
                    if (state.EntityManager.HasComponent<WallTag>(primaryEntity) &&
                        state.EntityManager.HasComponent<ExplosionData>(primaryEntity))
                    {
                        SendSelfDestructRpc(ref state, primaryEntity);

                        // Command 상태로 복귀
                        state.EntityManager.SetComponentData(userStateEntity, new UserState
                        {
                            CurrentState = UserContext.Command
                        });
                        return;
                    }
                }

                // 배럭 생산 (Q키 - 첫 번째 유닛, W키 - 두 번째 유닛)
                if (state.EntityManager.HasComponent<BarracksTag>(primaryEntity))
                {
                    if (keyboard.qKey.wasPressedThisFrame)
                    {
                        SendProduceUnitRpc(ref state, primaryEntity, 0);

                        // Command 상태로 복귀
                        state.EntityManager.SetComponentData(userStateEntity, new UserState
                        {
                            CurrentState = UserContext.Command
                        });
                        return;
                    }

                    if (keyboard.wKey.wasPressedThisFrame)
                    {
                        SendProduceUnitRpc(ref state, primaryEntity, 1);

                        // Command 상태로 복귀
                        state.EntityManager.SetComponentData(userStateEntity, new UserState
                        {
                            CurrentState = UserContext.Command
                        });
                        return;
                    }
                }
            }
        }

        private void SendSelfDestructRpc(ref SystemState state, Entity entity)
        {
            if (!state.EntityManager.HasComponent<GhostInstance>(entity)) return;

            var ghostInstance = state.EntityManager.GetComponentData<GhostInstance>(entity);

            var rpcEntity = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponentData(rpcEntity, new SelfDestructRequestRpc
            {
                TargetGhostId = ghostInstance.ghostId
            });
            state.EntityManager.AddComponent<SendRpcCommandRequest>(rpcEntity);

            UnityEngine.Debug.Log($"[StructureCommandInput] Sent SelfDestructRequestRpc for GhostId: {ghostInstance.ghostId}");
        }

        private void SendProduceUnitRpc(ref SystemState state, Entity entity, int unitIndex)
        {
            if (!state.EntityManager.HasComponent<GhostInstance>(entity)) return;

            // 생산 가능 유닛 버퍼 확인
            if (!state.EntityManager.HasBuffer<ProducibleUnitElement>(entity)) return;

            var unitBuffer = state.EntityManager.GetBuffer<ProducibleUnitElement>(entity);
            if (unitIndex >= unitBuffer.Length)
            {
                UnityEngine.Debug.LogWarning($"[StructureCommandInput] UnitIndex {unitIndex} is out of range");
                return;
            }

            var ghostInstance = state.EntityManager.GetComponentData<GhostInstance>(entity);

            var rpcEntity = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponentData(rpcEntity, new ProduceUnitRequestRpc
            {
                BarracksGhostId = ghostInstance.ghostId,
                UnitIndex = unitIndex
            });
            state.EntityManager.AddComponent<SendRpcCommandRequest>(rpcEntity);

            UnityEngine.Debug.Log($"[StructureCommandInput] Sent ProduceUnitRequestRpc for GhostId: {ghostInstance.ghostId}, UnitIndex: {unitIndex}");
        }
    }
}
