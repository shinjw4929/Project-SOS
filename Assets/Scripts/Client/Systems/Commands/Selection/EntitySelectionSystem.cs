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
    /// <summary>
    /// 엔티티 선택 처리 시스템
    /// - Phase가 PendingClick/PendingBox일 때만 동작 (이벤트 기반)
    /// - 처리 후 Phase를 Idle로 복귀
    /// </summary>
    [UpdateInGroup(typeof(GhostInputSystemGroup))]
    [UpdateAfter(typeof(SelectionInputSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct EntitySelectionSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkStreamInGame>();
            state.RequireForUpdate<SelectionState>();
            state.RequireForUpdate<PhysicsWorldSingleton>();
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
            state.RequireForUpdate<UserState>();
            state.RequireForUpdate<NetworkId>();
        }

        public void OnUpdate(ref SystemState state)
        {
            ref var selectionState = ref SystemAPI.GetSingletonRW<SelectionState>().ValueRW;
            ref var userState = ref SystemAPI.GetSingletonRW<UserState>().ValueRW;
            // ESC 키 → 선택 해제
            var keyboard = Keyboard.current;
            if (keyboard != default && keyboard.escapeKey.wasPressedThisFrame)
            {
                // Command 상태에서만 선택 해제 (StructureMenu 등에서는 다른 시스템이 처리)
                if (userState.CurrentState == UserContext.Command)
                {
                    var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                        .CreateCommandBuffer(state.WorldUnmanaged);
                    DeselectAll(ref state, ecb);
                    return;
                }
            }

            // 이벤트 기반: PendingClick 또는 PendingBox일 때만 처리
            if (selectionState.Phase == SelectionPhase.PendingClick)
            {
                SetUserStateIdle(ref userState);
                HandleSingleClick(ref state, ref selectionState);
                selectionState.Phase = SelectionPhase.Idle;
            }
            else if (selectionState.Phase == SelectionPhase.PendingBox)
            {
                SetUserStateIdle(ref userState);
                HandleBoxSelection(ref state, ref selectionState);
                selectionState.Phase = SelectionPhase.Idle;
            }
        }

        private void HandleSingleClick(ref SystemState state, ref SelectionState selectionState)
        {
            if (!Camera.main) return; // Unity Object는 implicit bool 사용
            if (!SystemAPI.TryGetSingleton<NetworkId>(out var myNetworkId)) return;

            // 클릭 위치에서 레이캐스트
            float2 clickPos = selectionState.StartScreenPos;
            UnityEngine.Ray ray = Camera.main.ScreenPointToRay(new Vector3(clickPos.x, clickPos.y, 0));

            if (!SystemAPI.TryGetSingleton<PhysicsWorldSingleton>(out var physicsWorld)) return;

            var collisionWorld = physicsWorld.PhysicsWorld.CollisionWorld;
            var raycastInput = new RaycastInput
            {
                Start = ray.origin,
                End = ray.origin + ray.direction * 1000f,
                Filter = CollisionFilter.Default
            };

            var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged);

            if (collisionWorld.CastRay(raycastInput, out Unity.Physics.RaycastHit hit))
            {
                Entity hitEntity = hit.Entity;

                // 부모 엔티티 찾기 (Collider가 자식일 경우)
                hitEntity = FindSelectableEntity(ref state, hitEntity);

                bool isSelectable = state.EntityManager.HasComponent<UnitTag>(hitEntity) ||
                                    state.EntityManager.HasComponent<StructureTag>(hitEntity);

                if (isSelectable)
                {
                    // 기존 선택 해제 후 새 엔티티 선택
                    DeselectAll(ref state, ecb);
                    SelectEntity(ref state, ecb, hitEntity);
                }
                else
                {
                    // 선택 불가능한 대상 클릭 → 선택 해제
                    DeselectAll(ref state, ecb);
                }
            }
            else
            {
                // 허공 클릭 → 선택 해제
                DeselectAll(ref state, ecb);
            }
        }

        private void HandleBoxSelection(ref SystemState state, ref SelectionState selectionState)
        {
            if (!Camera.main) return; // Unity Object는 implicit bool 사용
            if (!SystemAPI.TryGetSingleton<NetworkId>(out var myNetworkId)) return;

            int myTeamId = myNetworkId.Value;
            var mainCamera = Camera.main;

            float2 min = math.min(selectionState.StartScreenPos, selectionState.CurrentScreenPos);
            float2 max = math.max(selectionState.StartScreenPos, selectionState.CurrentScreenPos);

            var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged);

            // 기존 선택 해제
            DeselectAll(ref state, ecb);

            // 박스 내 유닛 선택 (내 소유 유닛만)
            foreach (var (transform, team, entity) in SystemAPI.Query<RefRO<LocalTransform>, RefRO<Team>>()
                .WithAll<GhostOwnerIsLocal, UnitTag>()
                .WithEntityAccess())
            {
                if (team.ValueRO.teamId != myTeamId) continue;

                Vector3 worldPos = transform.ValueRO.Position;
                Vector3 screenPos3D = mainCamera.WorldToScreenPoint(worldPos);

                // 카메라 뒤 제외
                if (screenPos3D.z < 0) continue;

                float2 screenPos = new float2(screenPos3D.x, screenPos3D.y);

                // 박스 범위 체크
                if (screenPos.x >= min.x && screenPos.x <= max.x &&
                    screenPos.y >= min.y && screenPos.y <= max.y)
                {
                    SelectEntity(ref state, ecb, entity);
                }
            }
        }

        private void SetUserStateIdle(ref UserState userState)
        {
            userState.CurrentState = UserContext.Command;
        }
        
        /// <summary>
        /// 선택 가능한 부모 엔티티 찾기 (Collider가 자식일 경우)
        /// </summary>
        private Entity FindSelectableEntity(ref SystemState state, Entity entity)
        {
            bool isSelectable = state.EntityManager.HasComponent<UnitTag>(entity) ||
                                state.EntityManager.HasComponent<StructureTag>(entity);

            if (!isSelectable && state.EntityManager.HasComponent<Parent>(entity))
            {
                return state.EntityManager.GetComponentData<Parent>(entity).Value;
            }

            return entity;
        }

        /// <summary>
        /// 엔티티 선택
        /// </summary>
        private void SelectEntity(ref SystemState state, EntityCommandBuffer ecb, Entity entity)
        {
            ecb.SetComponentEnabled<Selected>(entity, true);
        }

        /// <summary>
        /// 모든 선택 해제
        /// </summary>
        private void DeselectAll(ref SystemState state, EntityCommandBuffer ecb)
        {
            foreach (var (_, entity) in SystemAPI.Query<RefRO<Selected>>()
                .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                .WithEntityAccess())
            {
                if (state.EntityManager.IsComponentEnabled<Selected>(entity))
                {
                    ecb.SetComponentEnabled<Selected>(entity, false);
                }
            }
        }
    }
}