using Unity.Entities;
using Unity.NetCode;
using Unity.Collections;
using UnityEngine.InputSystem;
using Shared;

namespace Client
{
    [UpdateInGroup(typeof(GhostInputSystemGroup))]
    [UpdateAfter(typeof(SelectedEntityInfoUpdateSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct StructureCommandInputSystem : ISystem
    {
        // 1. 데이터 접근을 위한 Lookup 필드 선언
        [ReadOnly] private BufferLookup<AvailableUnit> _availableUnitLookup;
        [ReadOnly] private ComponentLookup<GhostInstance> _ghostInstanceLookup;
        [ReadOnly] private ComponentLookup<ExplosionData> _explosionDataLookup;
        [ReadOnly] private ComponentLookup<ProductionFacilityTag> _productionFacilityLookup;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkStreamInGame>();
            state.RequireForUpdate<SelectedEntityInfoState>();
            state.RequireForUpdate<UnitCatalog>();
            state.RequireForUpdate<UserState>();
            state.RequireForUpdate<UnitPrefabIndexMap>();

            // 2. Lookup 초기화
            _availableUnitLookup = state.GetBufferLookup<AvailableUnit>(true);
            _ghostInstanceLookup = state.GetComponentLookup<GhostInstance>(true);
            _explosionDataLookup = state.GetComponentLookup<ExplosionData>(true);
            _productionFacilityLookup = state.GetComponentLookup<ProductionFacilityTag>(true);
        }

        public void OnUpdate(ref SystemState state)
        {
            var keyboard = Keyboard.current;
            if (keyboard == default) return;

            // 3. Lookup 업데이트 (프레임마다 최신 상태 반영)
            _availableUnitLookup.Update(ref state);
            _ghostInstanceLookup.Update(ref state);
            _explosionDataLookup.Update(ref state);
            _productionFacilityLookup.Update(ref state);

            ref var userState = ref SystemAPI.GetSingletonRW<UserState>().ValueRW;
            var selection = SystemAPI.GetSingleton<SelectedEntityInfoState>();

            switch (userState.CurrentState)
            {
                case UserContext.Command:
                    HandleCommandState(keyboard, ref userState, selection);
                    break;

                case UserContext.StructureActionMenu:
                    HandleMenuState(ref state, keyboard, ref userState, selection);
                    break;
            }
        }

        private void HandleCommandState(Keyboard keyboard, ref UserState userState, SelectedEntityInfoState selection)
        {
            if (!keyboard.qKey.wasPressedThisFrame) return;

            if (selection.Category != SelectionCategory.Structure ||
                !selection.IsOwnedSelection ||
                selection.PrimaryEntity == Entity.Null)
            {
                return;
            }

            userState.CurrentState = UserContext.StructureActionMenu;
        }

        private void HandleMenuState(ref SystemState state, Keyboard keyboard, ref UserState userState, SelectedEntityInfoState selection)
        {
            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                userState.CurrentState = UserContext.Command;
                return;
            }

            // 엔티티 존재 여부 확인 (Lookup 사용이 더 빠름)
            if (selection.PrimaryEntity == Entity.Null || !_ghostInstanceLookup.HasComponent(selection.PrimaryEntity))
            {
                userState.CurrentState = UserContext.Command;
                return;
            }

            Entity targetEntity = selection.PrimaryEntity;

            // 3. 자폭 명령 (Lookup으로 컴포넌트 확인)
            if (keyboard.rKey.wasPressedThisFrame && _explosionDataLookup.HasComponent(targetEntity))
            {
                SendSelfDestructRpc(ref state, targetEntity);
                userState.CurrentState = UserContext.Command;
                return;
            }

            // 4. 유닛 생산 (Lookup으로 태그 확인)
            if (_productionFacilityLookup.HasComponent(targetEntity))
            {
                int localIndex = -1;
                if (keyboard.qKey.wasPressedThisFrame) localIndex = 0;
                else if (keyboard.wKey.wasPressedThisFrame) localIndex = 1;
                else if (keyboard.eKey.wasPressedThisFrame) localIndex = 2;
                
                if (localIndex != -1)
                {
                    int globalIndex = TryGetGlobalUnitIndex(targetEntity, localIndex);
                    
                    if (globalIndex >= 0)
                    {
                        SendProduceUnitRpc(ref state, targetEntity, globalIndex);
                        // userState.CurrentState = UserContext.Command; 
                    }
                }
            }
        }

        // --- Helper Methods ---

        private int TryGetGlobalUnitIndex(Entity facilityEntity, int localIndex)
        {
            // EntityManager 대신 Lookup을 통해 버퍼 접근
            if (!_availableUnitLookup.TryGetBuffer(facilityEntity, out var buffer)) return -1;
            if (localIndex < 0 || localIndex >= buffer.Length) return -1;

            Entity prefabEntity = buffer[localIndex].PrefabEntity;

            // Managed API 호출 (Main Thread Only)
            var indexMap = SystemAPI.ManagedAPI.GetSingleton<UnitPrefabIndexMap>().Map;

            if (indexMap.TryGetValue(prefabEntity, out int globalIndex))
            {
                return globalIndex;
            }

            UnityEngine.Debug.LogWarning($"Prefab not found in Catalog: {prefabEntity}");
            return -1;
        }

        private void SendSelfDestructRpc(ref SystemState state, Entity entity)
        {
            // Lookup을 통해 GhostID 가져오기
            if (!_ghostInstanceLookup.TryGetComponent(entity, out var ghost)) return;

            var rpcEntity = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponentData(rpcEntity, new SelfDestructRequestRpc
            {
                TargetGhostId = ghost.ghostId
            });
            state.EntityManager.AddComponent<SendRpcCommandRequest>(rpcEntity);
        }

        private void SendProduceUnitRpc(ref SystemState state, Entity entity, int unitIndex)
        {
            if (!_ghostInstanceLookup.TryGetComponent(entity, out var ghost)) return;

            var rpcEntity = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponentData(rpcEntity, new ProduceUnitRequestRpc
            {
                StructureGhostId = ghost.ghostId,
                UnitIndex = unitIndex
            });
            state.EntityManager.AddComponent<SendRpcCommandRequest>(rpcEntity);
        }
    }
}