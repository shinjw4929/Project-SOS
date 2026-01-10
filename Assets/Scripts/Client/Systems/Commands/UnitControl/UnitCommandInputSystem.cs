using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Physics;
using Unity.Physics.Systems;
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
    [UpdateAfter(typeof(GhostIdLookupSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct UnitCommandInputSystem : ISystem
    {
        private ComponentLookup<PendingBuildRequest> _pendingBuildLookup;
        private ComponentLookup<GhostInstance> _ghostInstanceLookup;
        private ComponentLookup<ResourceNodeTag> _resourceNodeTagLookup;
        private ComponentLookup<WorkerTag> _workerTagLookup;
        private ComponentLookup<ResourceCenterTag> _resourceCenterTagLookup;
        private ComponentLookup<WorkerState> _workerStateLookup;
        private EntityQuery _physicsWorldQuery;
        
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkStreamInGame>();
            state.RequireForUpdate<NetworkId>();
            state.RequireForUpdate<UserState>();
            state.RequireForUpdate<BeginSimulationEntityCommandBufferSystem.Singleton>();
            state.RequireForUpdate<GhostIdMap>();
            state.RequireForUpdate<NetworkTime>();
            state.RequireForUpdate<PhysicsWorldSingleton>();

            _pendingBuildLookup = state.GetComponentLookup<PendingBuildRequest>(true);
            _ghostInstanceLookup = state.GetComponentLookup<GhostInstance>(true);
            _resourceNodeTagLookup = state.GetComponentLookup<ResourceNodeTag>(true);
            _workerTagLookup = state.GetComponentLookup<WorkerTag>(true);
            _resourceCenterTagLookup = state.GetComponentLookup<ResourceCenterTag>(true);
            _workerStateLookup = state.GetComponentLookup<WorkerState>(true);
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
            _resourceNodeTagLookup.Update(ref state);
            _workerTagLookup.Update(ref state);
            _resourceCenterTagLookup.Update(ref state);
            _workerStateLookup.Update(ref state);

            ProcessMouseInput(ref state);
        }

        private void ProcessMouseInput(ref SystemState state)
        {
            var mouse = Mouse.current;
            if (mouse == default || !Camera.main) return;

            // 우클릭했을 때만 명령 전송
            if (mouse.rightButton.wasPressedThisFrame)
            {
                // 1. 스크린 좌표를 월드 Ray로 변환 (Unity API 사용)
                float2 mousePos = mouse.position.ReadValue();
                UnityEngine.Ray unityRay = Camera.main.ScreenPointToRay(new Vector3(mousePos.x, mousePos.y, 0));

                // 2. DOTS Physics Raycast 준비
                // PhysicsWorldSingleton을 통해 ECS 물리 월드에 접근
                var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;

                var rayInput = new RaycastInput
                {
                    Start = unityRay.origin,
                    End = unityRay.origin + unityRay.direction * 1000f,
                    Filter = CollisionFilter.Default
                };

                // 3. CastRay 수행 (매우 빠름, O(1) ~ O(logN))
                if (physicsWorld.CastRay(rayInput, out Unity.Physics.RaycastHit hit))
                {
                    // Hit된 Entity를 바로 가져옴 (루프 불필요!)
                    Entity hitEntity = hit.Entity;
                    int targetGhostId = 0;

                    // Hit된 엔티티가 Ghost(네트워크 객체)인지 확인
                    if (_ghostInstanceLookup.HasComponent(hitEntity))
                    {
                        targetGhostId = _ghostInstanceLookup[hitEntity].ghostId;
                    }

                    // 4. 명령 전송
                    SendCommandToSelectedUnits(ref state, hit.Position, targetGhostId);
                    return;
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

            // GhostIdMap 획득 (UpdateAfter로 Job 동기화 보장됨)
            if (!SystemAPI.TryGetSingleton<GhostIdMap>(out var ghostIdMapData))
                return;
            var ghostIdMap = ghostIdMapData.Map;

            // 타겟 엔티티 조회
            Entity targetEntity = Entity.Null;
            bool isResourceNode = false;
            bool isResourceCenter = false;

            if (targetGhostId != 0 && ghostIdMap.TryGetValue(targetGhostId, out targetEntity))
            {
                isResourceNode = _resourceNodeTagLookup.HasComponent(targetEntity);
                isResourceCenter = _resourceCenterTagLookup.HasComponent(targetEntity);
            }

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

                // 3. Worker + ResourceNode 조합일 때 GatherRequestRpc 전송
                if (isResourceNode && _workerTagLookup.HasComponent(entity))
                {
                    if (_ghostInstanceLookup.TryGetComponent(entity, out var workerGhost))
                    {
                        Entity rpcEntity = ecb.CreateEntity();
                        ecb.AddComponent(rpcEntity, new GatherRequestRpc
                        {
                            WorkerGhostId = workerGhost.ghostId,
                            ResourceNodeGhostId = targetGhostId,
                            ReturnPointGhostId = 0 // 자동 선택
                        });
                        ecb.AddComponent<SendRpcCommandRequest>(rpcEntity);
                    }
                }

                // 4. Worker + ResourceCenter + 자원 소지 시 ReturnResourceRequestRpc 전송
                if (isResourceCenter && _workerTagLookup.HasComponent(entity))
                {
                    // 자원을 들고 있는지 확인
                    if (_workerStateLookup.TryGetComponent(entity, out var workerState) &&
                        workerState.CarriedAmount > 0)
                    {
                        if (_ghostInstanceLookup.TryGetComponent(entity, out var workerGhost))
                        {
                            Entity rpcEntity = ecb.CreateEntity();
                            ecb.AddComponent(rpcEntity, new ReturnResourceRequestRpc
                            {
                                WorkerGhostId = workerGhost.ghostId,
                                ResourceCenterGhostId = targetGhostId
                            });
                            ecb.AddComponent<SendRpcCommandRequest>(rpcEntity);
                        }
                    }
                }

                // 5. 건설 대기 상태였다면 취소 (이동 명령이 우선이므로)
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