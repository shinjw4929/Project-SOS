using Unity.Entities;
using Unity.NetCode;
using UnityEngine.InputSystem;
using Unity.Mathematics;
using Shared;

namespace Client
{
    [UpdateInGroup(typeof(GhostInputSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct ConstructionMenuInputSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkStreamInGame>();
            state.RequireForUpdate<UserState>();
            state.RequireForUpdate<StructureCatalog>();
            state.RequireForUpdate<SelectedEntityInfoState>();
            state.RequireForUpdate<StructurePrefabIndexMap>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            // 1. 싱글톤 참조 가져오기
            ref var userState = ref SystemAPI.GetSingletonRW<UserState>().ValueRW;
            ref var previewState = ref SystemAPI.GetSingletonRW<StructurePreviewState>().ValueRW;
            var selectedEntityInfoState = SystemAPI.GetSingleton<SelectedEntityInfoState>();

            // 2. 상태별 입력 처리
            switch (userState.CurrentState)
            {
                case UserContext.Command:
                    HandleCommandState(ref state, keyboard, ref userState, selectedEntityInfoState);
                    break;
                    
                case UserContext.BuildMenu:
                    HandleBuildMenuState(ref state, keyboard, ref userState, ref previewState, selectedEntityInfoState);
                    break;
                    
                case UserContext.Construction:
                    HandleConstructionState(keyboard, ref userState, ref previewState);
                    break;
            }
        }

        // [상태 1] 기본 명령 상태 -> 건설 메뉴 진입
        private void HandleCommandState(ref SystemState state, Keyboard keyboard, ref UserState userState, SelectedEntityInfoState selectedEntityInfoState)
        {
            if (!keyboard.qKey.wasPressedThisFrame) return;

            // 유효성 검사: 선택된 것이 있고, 1개이며, 내 소유여야 함
            if (selectedEntityInfoState.PrimaryEntity == Entity.Null || 
                selectedEntityInfoState.SelectedCount != 1 || 
                !selectedEntityInfoState.IsOwnedSelection) return;

            // 엔티티가 실제로 존재하는지 확인
            if (!state.EntityManager.Exists(selectedEntityInfoState.PrimaryEntity)) return;

            // BuilderTag 확인
            if (!state.EntityManager.HasComponent<BuilderTag>(selectedEntityInfoState.PrimaryEntity)) return;

            // 건설 가능한 목록이 있는지 확인
            if (state.EntityManager.HasBuffer<AvailableStructure>(selectedEntityInfoState.PrimaryEntity))
            {
                userState.CurrentState = UserContext.BuildMenu;
            }
        }

        // [상태 2] 건설 메뉴 -> 건물 선택
        private void HandleBuildMenuState(ref SystemState state, Keyboard keyboard, ref UserState userState, ref StructurePreviewState previewState, SelectedEntityInfoState selection)
        {
            // ESC: 취소
            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                userState.CurrentState = UserContext.Command;
                return;
            }

            // 키 입력 -> 인덱스 매핑
            int targetLocalIndex = -1;
            if (keyboard.qKey.wasPressedThisFrame) targetLocalIndex = 0;
            else if (keyboard.wKey.wasPressedThisFrame) targetLocalIndex = 1;
            else if (keyboard.eKey.wasPressedThisFrame) targetLocalIndex = 2;
            else if (keyboard.rKey.wasPressedThisFrame) targetLocalIndex = 3;

            if (targetLocalIndex != -1)
            {
                // 선택 시도
                TrySelectStructureFromUnit(
                    ref state,
                    ref userState,
                    ref previewState,
                    state.EntityManager,
                    selection.PrimaryEntity,
                    targetLocalIndex
                );
            }
        }

        // [상태 3] 건설 중 -> 취소
        private void HandleConstructionState(Keyboard keyboard, ref UserState userState, ref StructurePreviewState previewState)
        {
            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                // 건설 취소 -> 기본 상태로 복귀
                userState.CurrentState = UserContext.Command;
                previewState.SelectedPrefab = Entity.Null;
                previewState.IsValidPlacement = false;
            }
        }

        private void TrySelectStructureFromUnit(
            ref SystemState state,
            ref UserState userState,
            ref StructurePreviewState previewState,
            EntityManager entityManager,
            Entity builderEntity,
            int localIndex)
        {
            // 엔티티 및 버퍼 존재 여부 재확인
            if (!entityManager.Exists(builderEntity) || !entityManager.HasBuffer<AvailableStructure>(builderEntity)) return;

            var structureBuffer = entityManager.GetBuffer<AvailableStructure>(builderEntity);

            if (localIndex < 0 || localIndex >= structureBuffer.Length) return;

            Entity targetPrefab = structureBuffer[localIndex].PrefabEntity;

            // 싱글톤에서 인덱스 맵 가져오기
            var indexMap = SystemAPI.ManagedAPI.GetSingleton<StructurePrefabIndexMap>().Map;

            // 전역 인덱스 맵에서 조회
            if (indexMap.TryGetValue(targetPrefab, out int globalIndex))
            {
                userState.CurrentState = UserContext.Construction;

                previewState.SelectedPrefab = targetPrefab;
                previewState.SelectedPrefabIndex = globalIndex;
                previewState.GridPosition = new int2(-9999, -9999); // 초기값 설정
                previewState.IsValidPlacement = false;
            }
        }
    }
}