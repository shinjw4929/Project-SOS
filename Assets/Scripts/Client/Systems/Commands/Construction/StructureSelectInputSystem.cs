using Unity.Entities;
using Unity.NetCode;
using UnityEngine.InputSystem;
using Unity.Mathematics;
using Shared;

namespace Client
{
    [UpdateInGroup(typeof(GhostInputSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct StructureSelectInputSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkStreamInGame>();
            state.RequireForUpdate<UserState>();
            state.RequireForUpdate<StructureEntitiesReferences>();

            if (!SystemAPI.HasSingleton<StructurePreviewState>())
            {
                var entity = state.EntityManager.CreateEntity();
                state.EntityManager.AddComponentData(entity, new StructurePreviewState());
            }
        }

        public void OnUpdate(ref SystemState state)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            ref var userState = ref SystemAPI.GetSingletonRW<UserState>().ValueRW;
            ref var previewState = ref SystemAPI.GetSingletonRW<StructurePreviewState>().ValueRW;
            
            var refsEntity = SystemAPI.GetSingletonEntity<StructureEntitiesReferences>();
            var prefabBuffer = SystemAPI.GetBuffer<StructurePrefabElement>(refsEntity);

            // -----------------------------------------------------------------
            // 상태별 입력 처리
            // -----------------------------------------------------------------
            
            // 1. 기본 상태일 때 (Q를 눌러 건설 메뉴 진입)
            if (userState.CurrentState == UserContext.Command)
            {
                if (keyboard.qKey.wasPressedThisFrame)
                {
                    // 건설 메뉴 상태로 진입 (아직 건물 선택 안 함)
                    userState.CurrentState = UserContext.BuildMenu;
                    // Debug.Log("Build Menu Opened");
                }
            }
            // 2. 건설 메뉴 상태일 때 (Q/W로 건물 선택)
            else if (userState.CurrentState == UserContext.BuildMenu)
            {
                if (keyboard.qKey.wasPressedThisFrame)
                {
                    // Q -> Q : Wall 선택
                    SetConstructionMode(ref userState, ref previewState, prefabBuffer, state.EntityManager, isWall: true);
                }
                else if (keyboard.wKey.wasPressedThisFrame)
                {
                    // Q -> W : Barracks 선택
                    SetConstructionMode(ref userState, ref previewState, prefabBuffer, state.EntityManager, isWall: false);
                }
                else if (keyboard.escapeKey.wasPressedThisFrame)
                {
                    // ESC : 메뉴 닫기 (Command로 복귀)
                    userState.CurrentState = UserContext.Command;
                }
            }
            // 3. 건설 중일 때 (ESC로 취소)
            else if (userState.CurrentState == UserContext.Construction)
            {
                if (keyboard.escapeKey.wasPressedThisFrame)
                {
                    userState.CurrentState = UserContext.Command; // 바로 Command로 가거나 BuildMenu로 가도록 설정 가능
                    previewState.SelectedPrefab = Entity.Null;
                }
            }
        }

        /// <summary>
        /// 버퍼에서 원하는 태그(Wall/Barracks)를 가진 프리팹을 찾아 설정하는 헬퍼 함수
        /// </summary>
        private void SetConstructionMode(ref UserState userState, ref StructurePreviewState previewState, 
            DynamicBuffer<StructurePrefabElement> buffer, EntityManager em, bool isWall)
        {
            userState.CurrentState = UserContext.Construction;
    
            Entity foundPrefab = Entity.Null;
            int foundIndex = -1; // 인덱스 초기화

            // 버퍼를 순회하며 인덱스(i)를 같이 추적
            for (int i = 0; i < buffer.Length; i++)
            {
                Entity prefab = buffer[i].PrefabEntity;
                if (prefab == Entity.Null || !em.Exists(prefab)) continue;

                if (isWall)
                {
                    if (em.HasComponent<WallTag>(prefab)) 
                    {
                        foundPrefab = prefab;
                        foundIndex = i; // 인덱스 저장!
                        break;
                    }
                }
                else // Barracks
                {
                    if (em.HasComponent<BarracksTag>(prefab))
                    {
                        foundPrefab = prefab;
                        foundIndex = i; // 인덱스 저장!
                        break;
                    }
                }
            }

            if (foundPrefab != Entity.Null)
            {
                previewState.SelectedPrefab = foundPrefab;
                previewState.SelectedPrefabIndex = foundIndex; // State에 저장

                // GridPosition 무효화 (이전 위치에서 깜빡이는 문제 방지)
                previewState.GridPosition = new int2(-1000, -1000);
                previewState.IsValidPlacement = false;
            }
            else
            {
                UnityEngine.Debug.LogWarning("Prefab Not Found");
            }
        }
    }
}