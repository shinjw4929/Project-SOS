using Unity.Entities;
using Unity.NetCode;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Physics;
using UnityEngine;
using UnityEngine.InputSystem;
using Shared; // UnitTag, StructureTag, Team, Selected

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
            state.RequireForUpdate<UserState>();
            state.RequireForUpdate<NetworkId>();
        }

        public void OnUpdate(ref SystemState state)
        {
            // 내 팀 번호(NetworkId) 가져오기
            if (!SystemAPI.TryGetSingleton<NetworkId>(out var myNetworkId)) return;
            int myTeamId = myNetworkId.Value;

            var selectionState = SystemAPI.GetSingleton<SelectionState>();

            if (selectionState.Mode == SelectionMode.SingleClick)
            {
                HandleSingleClick(ref state, myTeamId);
            }
            else if (selectionState.Mode == SelectionMode.BoxDragging)
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
            var userState = SystemAPI.GetSingleton<UserState>();
            
            // 건설 모드 등에서는 선택 동작 방지
            if (collisionWorld.CastRay(raycastInput, out Unity.Physics.RaycastHit hit) && userState.CurrentState == UserContext.Command)
            {
                Entity hitEntity = hit.Entity;

                // [부모 찾기] 클릭한 대상이 유닛/건물의 일부(Collider)라면 본체(Parent)를 가져옴
                // 태그 검사 UnitTag, StructureTag
                bool isTarget = state.EntityManager.HasComponent<UnitTag>(hitEntity) ||
                                state.EntityManager.HasComponent<StructureTag>(hitEntity);
                                
                if (!isTarget && state.EntityManager.HasComponent<Parent>(hitEntity))
                {
                    hitEntity = state.EntityManager.GetComponentData<Parent>(hitEntity).Value;
                }

                // 다시 한 번 본체가 유효한지 확인
                bool isSelectable = state.EntityManager.HasComponent<UnitTag>(hitEntity) ||
                                    state.EntityManager.HasComponent<StructureTag>(hitEntity);
                                    
                if (isSelectable)
                {
                    // [싱글 클릭 전략]
                    // 1. 기존 선택 모두 해제
                    // 2. 적군이든 아군이든 정보 창을 띄우기 위해 선택 자체는 허용 (RTS 표준)
                    DeselectAll(ref state, ecb);

                    if (!state.EntityManager.HasComponent<Selected>(hitEntity)) 
                        ecb.AddComponent<Selected>(hitEntity);
                        
                    ecb.SetComponentEnabled<Selected>(hitEntity, true);
                }
                else
                {
                    // 땅바닥이나 장식물 클릭 시 선택 해제
                    DeselectAll(ref state, ecb);
                }
            }
            else
            {
                // 허공 클릭 시 선택 해제
                DeselectAll(ref state, ecb);
            }
            
            // 클릭 처리 후 Idle 상태로 복귀
            var stateRW = SystemAPI.GetSingletonRW<SelectionState>();
            stateRW.ValueRW.Mode = SelectionMode.Idle;
        }

        private void HandleBoxSelection(ref SystemState state, int myTeamId)
        {
            var selectionBox = SystemAPI.GetSingleton<SelectionBox>();
            var mainCamera = Camera.main;
            if (mainCamera == null) return;

            float2 min = math.min(selectionBox.StartScreenPos, selectionBox.CurrentScreenPos);
            float2 max = math.max(selectionBox.StartScreenPos, selectionBox.CurrentScreenPos);
            
            // 드래그 거리가 너무 짧으면 무시 (실수 방지)
            if (math.distance(min, max) < 10f) return;

            var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);

            // 박스 드래그 시작 시 기존 선택 해제
            DeselectAll(ref state, ecb);

            // [통합 루프 & 최적화]
            // 유닛과 건물을 각각 돌지 않고 WithAny로 한 번에 처리
            // WithAll<GhostOwnerIsLocal> : 내 소유권이 있는(내 팀인) 엔티티만 박스 선택 가능 (RTS 표준)
            foreach (var (transform, team, entity) in SystemAPI.Query<RefRO<LocalTransform>, RefRO<Team>>()
                .WithAll<GhostOwnerIsLocal>() 
                .WithAny<UnitTag, StructureTag>() // [변경] Tag 사용
                .WithEntityAccess())
            {
                // GhostOwnerIsLocal이 있어도 안전을 위해 팀 ID 이중 체크
                if (team.ValueRO.teamId != myTeamId) continue;

                Vector3 worldPos = transform.ValueRO.Position;
                Vector3 screenPos3D = mainCamera.WorldToScreenPoint(worldPos);

                // 카메라 뒤에 있는 객체 제외
                if (screenPos3D.z < 0) continue;

                float2 screenPos = new float2(screenPos3D.x, screenPos3D.y);

                // 박스 범위 체크
                if (screenPos.x >= min.x && screenPos.x <= max.x &&
                    screenPos.y >= min.y && screenPos.y <= max.y)
                {
                    if (!state.EntityManager.HasComponent<Selected>(entity)) 
                        ecb.AddComponent<Selected>(entity);
                        
                    ecb.SetComponentEnabled<Selected>(entity, true);
                }
            }
        }

        // [헬퍼 함수] 모든 선택 해제
        private void DeselectAll(ref SystemState state, EntityCommandBuffer ecb)
        {
            foreach (var (_, entity) in SystemAPI.Query<RefRO<Selected>>().WithEntityAccess())
            {
                ecb.SetComponentEnabled<Selected>(entity, false);
            }
        }
    }
}