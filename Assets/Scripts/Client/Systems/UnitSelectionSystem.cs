using Unity.Entities;
using Unity.NetCode;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Physics;
using UnityEngine;
using UnityEngine.InputSystem;
using Shared; 

namespace Client
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct UnitSelectionSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkStreamInGame>();
            state.RequireForUpdate<SelectionState>();
            state.RequireForUpdate<SelectionBox>();
            state.RequireForUpdate<PhysicsWorldSingleton>();
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
            // NetworkId는 필수 (팀 구분을 위해)
            state.RequireForUpdate<NetworkId>();
        }

        public void OnUpdate(ref SystemState state)
        {
            // 내 팀 번호(NetworkId) 가져오기
            if (!SystemAPI.TryGetSingleton<NetworkId>(out var myNetworkId)) return;
            int myTeamId = myNetworkId.Value;

            var selectionState = SystemAPI.GetSingleton<SelectionState>();

            if (selectionState.mode == SelectionMode.SingleClick)
            {
                HandleSingleClick(ref state, myTeamId);
            }
            else if (selectionState.mode == SelectionMode.BoxDragging)
            {
                HandleBoxSelection(ref state, myTeamId);
            }
        }
        
        private void HandleSingleClick(ref SystemState state, int myTeamId)
        {
            var mouse = Mouse.current;
            if (mouse == null || Camera.main == null) return;

            float2 mousePos = mouse.position.ReadValue();
            UnityEngine.Ray ray = Camera.main.ScreenPointToRay(new Vector3(mousePos.x, mousePos.y, 0));

            if (!SystemAPI.TryGetSingleton<PhysicsWorldSingleton>(out var physicsWorldSingleton)) return;

            CollisionWorld collisionWorld = physicsWorldSingleton.PhysicsWorld.CollisionWorld;
            RaycastInput raycastInput = new RaycastInput
            {
                Start = ray.origin,
                End = ray.origin + ray.direction * 1000f,
                Filter = CollisionFilter.Default
            };

            var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);

            // 레이캐스트 실행
            if (collisionWorld.CastRay(raycastInput, out Unity.Physics.RaycastHit hit))
            {
                Entity hitEntity = hit.Entity;

                // [부모 찾기] 클릭한 대상이 유닛의 일부(Collider)라면 본체(Parent)를 가져옴
                if (!state.EntityManager.HasComponent<Player>(hitEntity) && state.EntityManager.HasComponent<Parent>(hitEntity))
                {
                    hitEntity = state.EntityManager.GetComponentData<Parent>(hitEntity).Value;
                }

                // 1. 유닛(Player)을 클릭했는지 확인
                if (state.EntityManager.HasComponent<Player>(hitEntity))
                {
                    var playerData = state.EntityManager.GetComponentData<Player>(hitEntity);
                    
                    // 2. 팀 확인 (내 팀인가?)
                    if (playerData.TeamId == myTeamId)
                    {
                        // [아군 선택] 기존 선택 해제 후 새로 선택
                        DeselectAll(ref state, ecb);

                        if (!state.EntityManager.HasComponent<Selected>(hitEntity)) ecb.AddComponent<Selected>(hitEntity);
                        ecb.SetComponentEnabled<Selected>(hitEntity, true);
                    }
                }
                // 3. 유닛이 아닌 것(땅바닥, 장애물)을 클릭한 경우
                else
                {
                    // [땅 클릭] -> 모든 선택 해제
                    DeselectAll(ref state, ecb);
                    // Debug.Log(">>> Ground Clicked (Deselect All) <<<");
                }
            }
            else
            {
                // [허공 클릭] -> 모든 선택 해제 (Raycast가 아무것도 못 맞춘 경우)
                DeselectAll(ref state, ecb);
            }
            
            var stateRW = SystemAPI.GetSingletonRW<SelectionState>();
            stateRW.ValueRW.mode = SelectionMode.Idle;
        }

        private void HandleBoxSelection(ref SystemState state, int myTeamId)
        {
            var selectionBox = SystemAPI.GetSingleton<SelectionBox>();
            var mainCamera = Camera.main;
            if (mainCamera == null) return;

            float2 min = math.min(selectionBox.startScreenPos, selectionBox.currentScreenPos);
            float2 max = math.max(selectionBox.startScreenPos, selectionBox.currentScreenPos);
            
            if (math.distance(min, max) < 10f) return;

            var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);

            // 박스 드래그 시작 시 기존 선택 해제
            DeselectAll(ref state, ecb);

            foreach (var (transform, player, entity) in SystemAPI.Query<RefRO<LocalTransform>, RefRO<Player>>()
                .WithAll<GhostOwnerIsLocal>()
                .WithEntityAccess())
            {
                // 팀이 다르면 스킵
                if (player.ValueRO.TeamId != myTeamId) continue;

                Vector3 worldPos = transform.ValueRO.Position;
                Vector3 screenPos3D = mainCamera.WorldToScreenPoint(worldPos);
                
                if (screenPos3D.z < 0) continue;

                float2 unitScreenPos = new float2(screenPos3D.x, screenPos3D.y);

                if (unitScreenPos.x >= min.x && unitScreenPos.x <= max.x &&
                    unitScreenPos.y >= min.y && unitScreenPos.y <= max.y)
                {
                    if (!state.EntityManager.HasComponent<Selected>(entity)) ecb.AddComponent<Selected>(entity);
                    ecb.SetComponentEnabled<Selected>(entity, true);
                }
            }
        }

        // [헬퍼 함수] 모든 선택 해제 (코드 중복 방지)
        private void DeselectAll(ref SystemState state, EntityCommandBuffer ecb)
        {
            foreach (var (_, entity) in SystemAPI.Query<RefRO<Selected>>().WithEntityAccess())
            {
                ecb.SetComponentEnabled<Selected>(entity, false);
            }
        }
    }
}