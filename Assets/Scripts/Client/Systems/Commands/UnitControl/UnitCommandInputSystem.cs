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
    /// 사용자의 마우스/키보드 입력을 RPC로 변환하여 서버에 전송하는 시스템
    /// - 이동 명령: MoveRequestRpc
    /// - 공격 명령: AttackRequestRpc
    /// - 채집 명령: GatherRequestRpc
    /// - 반납 명령: ReturnResourceRequestRpc
    /// </summary>
    [UpdateInGroup(typeof(GhostInputSystemGroup))]
    [UpdateAfter(typeof(SelectedEntityInfoUpdateSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct UnitCommandInputSystem : ISystem
    {
        private ComponentLookup<GhostInstance> _ghostInstanceLookup;
        private ComponentLookup<ResourceNodeTag> _resourceNodeTagLookup;
        private ComponentLookup<WorkerTag> _workerTagLookup;
        private ComponentLookup<ResourceCenterTag> _resourceCenterTagLookup;
        private ComponentLookup<WorkerState> _workerStateLookup;
        private ComponentLookup<EnemyTag> _enemyTagLookup;
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

            _ghostInstanceLookup = state.GetComponentLookup<GhostInstance>(true);
            _resourceNodeTagLookup = state.GetComponentLookup<ResourceNodeTag>(true);
            _workerTagLookup = state.GetComponentLookup<WorkerTag>(true);
            _resourceCenterTagLookup = state.GetComponentLookup<ResourceCenterTag>(true);
            _workerStateLookup = state.GetComponentLookup<WorkerState>(true);
            _enemyTagLookup = state.GetComponentLookup<EnemyTag>(true);
        }

        public void OnUpdate(ref SystemState state)
        {
            var userState = SystemAPI.GetSingleton<UserState>();
            if (userState.CurrentState == UserContext.Dead) return;

            // Construction 모드에서는 입력 처리하지 않음
            // (StructurePlacementInputSystem에서 BuildKey 명령을 처리)
            if (userState.CurrentState == UserContext.Construction) return;

            // Lookup 업데이트
            _ghostInstanceLookup.Update(ref state);
            _resourceNodeTagLookup.Update(ref state);
            _workerTagLookup.Update(ref state);
            _resourceCenterTagLookup.Update(ref state);
            _workerStateLookup.Update(ref state);
            _enemyTagLookup.Update(ref state);

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

                    // 4. 명령 전송 (RPC 기반)
                    SendCommandToSelectedUnits(ref state, hit.Position, targetGhostId, hitEntity);
                }
            }
        }

        private void SendCommandToSelectedUnits(ref SystemState state, float3 goalPos, int targetGhostId, Entity hitEntity)
        {
            var ecb = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged);

            // 타겟 유형 확인
            bool isResourceNode = _resourceNodeTagLookup.HasComponent(hitEntity);
            bool isResourceCenter = _resourceCenterTagLookup.HasComponent(hitEntity);
            bool isEnemy = _enemyTagLookup.HasComponent(hitEntity);

            // 선택된 내 유닛들에게 명령 하달
            foreach (var (_, entity) in SystemAPI.Query<RefRO<UnitTag>>()
                .WithAll<Selected, GhostOwnerIsLocal>()
                .WithEntityAccess())
            {
                // 유닛의 GhostId 획득
                if (!_ghostInstanceLookup.TryGetComponent(entity, out var unitGhost))
                    continue;

                int unitGhostId = unitGhost.ghostId;

                // 1. Worker + ResourceNode 조합일 때 GatherRequestRpc 전송
                if (isResourceNode && _workerTagLookup.HasComponent(entity))
                {
                    Entity rpcEntity = ecb.CreateEntity();
                    ecb.AddComponent(rpcEntity, new GatherRequestRpc
                    {
                        WorkerGhostId = unitGhostId,
                        ResourceNodeGhostId = targetGhostId,
                        ReturnPointGhostId = 0 // 자동 선택
                    });
                    ecb.AddComponent<SendRpcCommandRequest>(rpcEntity);
                }
                // 2. Worker + ResourceCenter + 자원 소지 시 ReturnResourceRequestRpc 전송
                else if (isResourceCenter && _workerTagLookup.HasComponent(entity))
                {
                    // 자원을 들고 있는지 확인
                    if (_workerStateLookup.TryGetComponent(entity, out var workerState) &&
                        workerState.CarriedAmount > 0)
                    {
                        Entity rpcEntity = ecb.CreateEntity();
                        ecb.AddComponent(rpcEntity, new ReturnResourceRequestRpc
                        {
                            WorkerGhostId = unitGhostId,
                            ResourceCenterGhostId = targetGhostId
                        });
                        ecb.AddComponent<SendRpcCommandRequest>(rpcEntity);
                    }
                    else
                    {
                        // 자원이 없으면 이동으로 처리
                        Entity rpcEntity = ecb.CreateEntity();
                        ecb.AddComponent(rpcEntity, new MoveRequestRpc
                        {
                            UnitGhostId = unitGhostId,
                            TargetPosition = goalPos
                        });
                        ecb.AddComponent<SendRpcCommandRequest>(rpcEntity);
                    }
                }
                // 3. 적 클릭 시 AttackRequestRpc 전송
                else if (isEnemy && targetGhostId != 0)
                {
                    Entity rpcEntity = ecb.CreateEntity();
                    ecb.AddComponent(rpcEntity, new AttackRequestRpc
                    {
                        UnitGhostId = unitGhostId,
                        TargetGhostId = targetGhostId,
                        TargetPosition = goalPos
                    });
                    ecb.AddComponent<SendRpcCommandRequest>(rpcEntity);
                }
                // 4. 그 외 (땅 클릭 등) → MoveRequestRpc 전송
                else
                {
                    Entity rpcEntity = ecb.CreateEntity();
                    ecb.AddComponent(rpcEntity, new MoveRequestRpc
                    {
                        UnitGhostId = unitGhostId,
                        TargetPosition = goalPos
                    });
                    ecb.AddComponent<SendRpcCommandRequest>(rpcEntity);
                }
            }
        }
    }
}
