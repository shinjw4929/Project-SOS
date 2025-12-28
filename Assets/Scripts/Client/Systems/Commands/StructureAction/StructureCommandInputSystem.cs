using Unity.Entities;
using Unity.NetCode;
using Unity.Collections;
using UnityEngine.InputSystem;
using Shared;
using UnityEngine;

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
        // [Entity -> int] 검색표
        private NativeParallelHashMap<Entity, int> _prefabIndexMap;
        
        public void OnCreate(ref SystemState state)
        {
            // 맵 초기화 (64개 맵핑)
            _prefabIndexMap = new NativeParallelHashMap<Entity, int>(64, Allocator.Persistent);
            state.RequireForUpdate<NetworkStreamInGame>();
            state.RequireForUpdate<CurrentSelectionState>();
            state.RequireForUpdate<UnitCatalog>();
            state.RequireForUpdate<UserState>();
        }

        public void OnDestroy(ref SystemState state)
        {
            if (_prefabIndexMap.IsCreated) _prefabIndexMap.Dispose();
        }
        
        public void OnUpdate(ref SystemState state)
        {
            // 1. 해시맵 캐싱 (데이터가 로드되었고 맵이 비었을 때)
            if (_prefabIndexMap.IsEmpty && SystemAPI.HasSingleton<UnitCatalog>())
            {
                BuildIndexMap(ref state);
            }
            
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            ref var userState = ref SystemAPI.GetSingletonRW<UserState>().ValueRW;
            var currentSelectionState = SystemAPI.GetSingleton<CurrentSelectionState>();

            
            // Command 상태에서 건물 선택 + Q키 → StructureMenu
            if (userState.CurrentState == UserContext.Command)
            {
                if (keyboard.qKey.wasPressedThisFrame)
                {
                    if (currentSelectionState.Category == SelectionCategory.Structure &&
                        currentSelectionState.IsOwnedSelection &&
                        currentSelectionState.PrimaryEntity != Entity.Null)
                    {
                        // StructureMenu 상태로 전환
                        userState.CurrentState = UserContext.StructureActionMenu;
                    }
                }
                return;
            }

            // StructureMenu 상태
            if (userState.CurrentState == UserContext.StructureActionMenu)
            {
                // ESC → Command 복귀
                if (keyboard.escapeKey.wasPressedThisFrame)
                {
                    userState.CurrentState = UserContext.Command;
                    return;
                }

                // 선택이 해제되었거나 건물이 아니면 Command 복귀
                if (currentSelectionState.Category != SelectionCategory.Structure ||
                    !currentSelectionState.IsOwnedSelection ||
                    currentSelectionState.PrimaryEntity == Entity.Null)
                {
                    userState.CurrentState = UserContext.Command;
                    return;
                }

                var primaryEntity = currentSelectionState.PrimaryEntity;

                bool canExplode = state.EntityManager.HasComponent<ExplosionData>(primaryEntity);
                bool canProduce = state.EntityManager.HasComponent<ProductionFacilityTag>(primaryEntity);
                

                // 자폭 (R키)
                if (canExplode && keyboard.rKey.wasPressedThisFrame)
                {
                    SendSelfDestructRpc(ref state, primaryEntity);

                    // Command 상태로 복귀
                    userState.CurrentState = UserContext.Command;
                    return;
                }

                // 생산 (Q키 - 첫 번째 유닛, W키 - 두 번째 유닛)
                if (canProduce)
                {
                    int targetLocalIndex = -1;
                    if (keyboard.qKey.wasPressedThisFrame) targetLocalIndex = 0;
                    else if (keyboard.wKey.wasPressedThisFrame) targetLocalIndex = 1;

                    if (targetLocalIndex != -1)
                    {
                        int globalIndex = TrySelectUnitFromStructure(
                            state.EntityManager,
                            primaryEntity,
                            targetLocalIndex
                        );

                        // 유효한 인덱스인 경우에만 RPC 전송
                        if (globalIndex >= 0)
                        {
                            SendProduceUnitRpc(ref state, primaryEntity, globalIndex);
                        }
                        userState.CurrentState = UserContext.Command;
                        
                        if (keyboard.escapeKey.wasPressedThisFrame)
                        {
                            // ESC : 메뉴 닫기 (Command로 복귀)
                            userState.CurrentState = UserContext.Command;
                        }
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
        }

        private void SendProduceUnitRpc(ref SystemState state, Entity entity, int unitIndex)
        {
            if (!state.EntityManager.HasComponent<GhostInstance>(entity)) return;

            var ghostInstance = state.EntityManager.GetComponentData<GhostInstance>(entity);

            var rpcEntity = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponentData(rpcEntity, new ProduceUnitRequestRpc
            {
                StructureGhostId = ghostInstance.ghostId,
                UnitIndex = unitIndex
            });
            state.EntityManager.AddComponent<SendRpcCommandRequest>(rpcEntity);
        }
        
        /// <summary>
        /// 선택된 건물의 버퍼에서 N번째 유닛을 가져와 전역 인덱스로 변환
        /// </summary>
        private int TrySelectUnitFromStructure(
            EntityManager entityManager, 
            Entity productionFacilityEntity, 
            int localIndex)
        {
            // 1. 건물의 생산 유닛 목록 버퍼 가져오기
            var unitBuffer = entityManager.GetBuffer<AvailableUnit>(productionFacilityEntity);

            // 2. 인덱스 유효성 검사
            if (localIndex < 0 || localIndex >= unitBuffer.Length)
            {
                // 예외 처리
                return - 1;
            }

            // 3. 짓고자 하는 프리팹 확인
            Entity targetPrefab = unitBuffer[localIndex].PrefabEntity;

            // 4. 프리팹이 전역 카탈로그의 몇 번째인지 해시맵 조회 (RPC용)
            if (_prefabIndexMap.TryGetValue(targetPrefab, out int globalIndex))
            {
                return globalIndex;
            }
            else
            {
                UnityEngine.Debug.LogWarning($"Prefab {targetPrefab} not found in Global Catalog Map!");
                return -1;
            }
        }
        
        private void BuildIndexMap(ref SystemState state)
        {
            var catalogEntity = SystemAPI.GetSingletonEntity<UnitCatalog>();
            var buffer = SystemAPI.GetBuffer<UnitCatalogElement>(catalogEntity);

            _prefabIndexMap.Clear();
            for (int i = 0; i < buffer.Length; i++)
            {
                if(buffer[i].PrefabEntity != Entity.Null)
                {
                    // 같은 프리팹이 중복되면 덮어씌워지지만, 카탈로그는 고유하다고 가정
                    _prefabIndexMap.TryAdd(buffer[i].PrefabEntity, i);
                }
            }
        }
    }
}
