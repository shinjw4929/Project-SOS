using Unity.Entities;
using Unity.NetCode;
using UnityEngine.InputSystem;
using Shared;

namespace Client
{
    [UpdateInGroup(typeof(GhostInputSystemGroup))]
    [UpdateAfter(typeof(SelectionStateSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct StructureCommandInputSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkStreamInGame>();
            state.RequireForUpdate<CurrentSelectionState>();
            state.RequireForUpdate<UnitCatalog>();
            state.RequireForUpdate<UserState>();
            state.RequireForUpdate<UnitPrefabIndexMap>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            // 1. 데이터 가져오기 (RefRW 사용)
            ref var userState = ref SystemAPI.GetSingletonRW<UserState>().ValueRW;
            var selection = SystemAPI.GetSingleton<CurrentSelectionState>();

            // 2. 상태별 분기 처리
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

        // [상태 1] Command 모드 -> 메뉴 진입
        private void HandleCommandState(Keyboard keyboard, ref UserState userState, CurrentSelectionState selection)
        {
            // Q키 입력 확인
            if (!keyboard.qKey.wasPressedThisFrame) return;

            // 유효성 검사 (건물인가? 내 것인가? 선택된 것이 있는가?)
            if (selection.Category != SelectionCategory.Structure ||
                !selection.IsOwnedSelection ||
                selection.PrimaryEntity == Entity.Null)
            {
                return;
            }

            // 상태 전환
            userState.CurrentState = UserContext.StructureActionMenu;
        }

        // [상태 2] 메뉴 모드 -> 기능 수행
        private void HandleMenuState(ref SystemState state, Keyboard keyboard, ref UserState userState, CurrentSelectionState selection)
        {
            // 1. 공통 탈출 조건 (ESC 키)
            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                userState.CurrentState = UserContext.Command;
                return;
            }

            // 2. 선택 유효성 재검사 (메뉴 열린 상태에서 건물이 터졌을 수 있음)
            if (selection.PrimaryEntity == Entity.Null || !state.EntityManager.Exists(selection.PrimaryEntity))
            {
                userState.CurrentState = UserContext.Command;
                return;
            }

            Entity targetEntity = selection.PrimaryEntity;
            var em = state.EntityManager;

            // 3. 자폭 명령 (R키) - ExplosionData가 있는 경우만
            if (keyboard.rKey.wasPressedThisFrame && em.HasComponent<ExplosionData>(targetEntity))
            {
                SendSelfDestructRpc(ref state, targetEntity);
                userState.CurrentState = UserContext.Command; // 명령 후 복귀
                return;
            }

            // 4. 유닛 생산 (Q/W키) - ProductionFacilityTag가 있는 경우만
            if (em.HasComponent<ProductionFacilityTag>(targetEntity))
            {
                int localIndex = -1;
                if (keyboard.qKey.wasPressedThisFrame) localIndex = 0;
                else if (keyboard.wKey.wasPressedThisFrame) localIndex = 1;

                if (localIndex != -1)
                {
                    // 인덱스 변환 및 RPC 전송
                    int globalIndex = TryGetGlobalUnitIndex(ref state, em, targetEntity, localIndex);
                    
                    if (globalIndex >= 0)
                    {
                        SendProduceUnitRpc(ref state, targetEntity, globalIndex);
                        
                        // [기획 결정] 생산 명령 후 메뉴를 닫을 것인가?
                        // 보통 RTS에서는 연속 생산을 위해 닫지 않지만, 
                        // 여기서는 코드 흐름상 닫는 것으로 처리합니다. (필요 시 주석 처리)
                        // userState.CurrentState = UserContext.Command; 
                    }
                }
            }
        }

        // --- Helper Methods ---

        private int TryGetGlobalUnitIndex(ref SystemState state, EntityManager em, Entity facilityEntity, int localIndex)
        {
            // 버퍼 존재 확인
            if (!em.HasBuffer<AvailableUnit>(facilityEntity)) return -1;

            var buffer = em.GetBuffer<AvailableUnit>(facilityEntity);
            if (localIndex < 0 || localIndex >= buffer.Length) return -1;

            Entity prefabEntity = buffer[localIndex].PrefabEntity;

            // 싱글톤에서 인덱스 맵 가져오기
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
            // GhostInstance 확인 (네트워크 객체인지)
            if (!state.EntityManager.HasComponent<GhostInstance>(entity)) return;

            var ghostId = state.EntityManager.GetComponentData<GhostInstance>(entity).ghostId;
            var rpcEntity = state.EntityManager.CreateEntity();
            
            state.EntityManager.AddComponentData(rpcEntity, new SelfDestructRequestRpc
            {
                TargetGhostId = ghostId
            });
            state.EntityManager.AddComponent<SendRpcCommandRequest>(rpcEntity);
        }

        private void SendProduceUnitRpc(ref SystemState state, Entity entity, int unitIndex)
        {
            if (!state.EntityManager.HasComponent<GhostInstance>(entity)) return;

            var ghostId = state.EntityManager.GetComponentData<GhostInstance>(entity).ghostId;
            var rpcEntity = state.EntityManager.CreateEntity();
            
            state.EntityManager.AddComponentData(rpcEntity, new ProduceUnitRequestRpc
            {
                StructureGhostId = ghostId,
                UnitIndex = unitIndex
            });
            state.EntityManager.AddComponent<SendRpcCommandRequest>(rpcEntity);
        }
    }
}