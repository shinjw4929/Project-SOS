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
    /// Managed System으로 변경됨 (Camera 등 Unity 객체 접근 및 캐싱을 위해 필수)
    /// </summary>
    [UpdateInGroup(typeof(GhostInputSystemGroup))]
    [UpdateAfter(typeof(SelectedEntityInfoUpdateSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial class UnitCommandInputSystem : SystemBase // class, SystemBase 상속
    {
        private ComponentLookup<GhostInstance> _ghostInstanceLookup;
        private ComponentLookup<ResourceNodeTag> _resourceNodeTagLookup;
        private ComponentLookup<WorkerTag> _workerTagLookup;
        private ComponentLookup<ResourceCenterTag> _resourceCenterTagLookup;
        private ComponentLookup<WorkerState> _workerStateLookup;
        private ComponentLookup<EnemyTag> _enemyTagLookup;

        // SystemBase(Class)이므로 Camera 필드 저장 가능
        private Camera _cachedCamera;

        protected override void OnCreate()
        {
            RequireForUpdate<NetworkStreamInGame>();
            RequireForUpdate<NetworkId>();
            RequireForUpdate<UserState>();
            RequireForUpdate<BeginSimulationEntityCommandBufferSystem.Singleton>();
            RequireForUpdate<GhostIdMap>();
            RequireForUpdate<NetworkTime>();
            RequireForUpdate<PhysicsWorldSingleton>();

            _ghostInstanceLookup = GetComponentLookup<GhostInstance>(true);
            _resourceNodeTagLookup = GetComponentLookup<ResourceNodeTag>(true);
            _workerTagLookup = GetComponentLookup<WorkerTag>(true);
            _resourceCenterTagLookup = GetComponentLookup<ResourceCenterTag>(true);
            _workerStateLookup = GetComponentLookup<WorkerState>(true);
            _enemyTagLookup = GetComponentLookup<EnemyTag>(true);
        }

        protected override void OnUpdate()
        {
            var userState = SystemAPI.GetSingleton<UserState>();
            if (userState.CurrentState == UserContext.Dead || userState.CurrentState == UserContext.Construction) 
                return;

            var mouse = Mouse.current;
            if (mouse == null || !mouse.rightButton.wasPressedThisFrame) 
                return;

            // 카메라 캐싱 로직
            if (_cachedCamera == null)
            {
                _cachedCamera = Camera.main;
                if (_cachedCamera == null) return;
            }

            // SystemBase에서는 'this'를 통해 업데이트
            _ghostInstanceLookup.Update(this);
            _resourceNodeTagLookup.Update(this);
            _workerTagLookup.Update(this);
            _resourceCenterTagLookup.Update(this);
            _workerStateLookup.Update(this);
            _enemyTagLookup.Update(this);

            ProcessMouseInput(mouse);
        }

        private void ProcessMouseInput(Mouse mouse)
        {
            float2 mousePos = mouse.position.ReadValue();
            UnityEngine.Ray unityRay = _cachedCamera.ScreenPointToRay(new Vector3(mousePos.x, mousePos.y, 0));

            var physicsWorldSingleton = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
            var collisionWorld = physicsWorldSingleton.CollisionWorld;

            var rayInput = new RaycastInput
            {
                Start = unityRay.origin,
                End = unityRay.origin + unityRay.direction * 1000f,
                Filter = CollisionFilter.Default
            };

            if (collisionWorld.CastRay(rayInput, out Unity.Physics.RaycastHit hit))
            {
                Entity hitEntity = hit.Entity;
                int targetGhostId = 0;

                if (_ghostInstanceLookup.HasComponent(hitEntity))
                {
                    targetGhostId = _ghostInstanceLookup[hitEntity].ghostId;
                }

                bool isResourceNode = _resourceNodeTagLookup.HasComponent(hitEntity);
                bool isResourceCenter = _resourceCenterTagLookup.HasComponent(hitEntity);
                bool isEnemy = _enemyTagLookup.HasComponent(hitEntity);

                SendCommandToSelectedUnits(hit.Position, targetGhostId, isResourceNode, isResourceCenter, isEnemy);
            }
        }

        private void SendCommandToSelectedUnits(
            float3 goalPos, 
            int targetGhostId, 
            bool isResourceNode, 
            bool isResourceCenter, 
            bool isEnemy)
        {
            // SystemBase에서는 World.Unmanaged 접근 방식이 다름 (this.World.Unmanaged 아님)
            // SystemAPI를 통해 ECB Singleton 접근
            var ecb = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(World.Unmanaged);

            foreach (var (_, entity) in SystemAPI.Query<RefRO<UnitTag>>()
                .WithAll<Selected, GhostOwnerIsLocal>()
                .WithEntityAccess())
            {
                if (!_ghostInstanceLookup.TryGetComponent(entity, out var unitGhost))
                    continue;

                int unitGhostId = unitGhost.ghostId;
                bool isWorker = _workerTagLookup.HasComponent(entity);

                if (isResourceNode && isWorker)
                {
                    var rpcEntity = ecb.CreateEntity();
                    ecb.AddComponent(rpcEntity, new GatherRequestRpc
                    {
                        WorkerGhostId = unitGhostId,
                        ResourceNodeGhostId = targetGhostId,
                        ReturnPointGhostId = 0 
                    });
                    ecb.AddComponent<SendRpcCommandRequest>(rpcEntity);
                }
                else if (isResourceCenter && isWorker)
                {
                    bool hasResource = false;
                    if (_workerStateLookup.TryGetComponent(entity, out var workerState))
                    {
                        hasResource = workerState.CarriedAmount > 0;
                    }

                    if (hasResource)
                    {
                        var rpcEntity = ecb.CreateEntity();
                        ecb.AddComponent(rpcEntity, new ReturnResourceRequestRpc
                        {
                            WorkerGhostId = unitGhostId,
                            ResourceCenterGhostId = targetGhostId
                        });
                        ecb.AddComponent<SendRpcCommandRequest>(rpcEntity);
                    }
                    else
                    {
                        CreateMoveRpc(ecb, unitGhostId, goalPos);
                    }
                }
                else if (isEnemy && targetGhostId != 0)
                {
                    var rpcEntity = ecb.CreateEntity();
                    ecb.AddComponent(rpcEntity, new AttackRequestRpc
                    {
                        UnitGhostId = unitGhostId,
                        TargetGhostId = targetGhostId,
                        TargetPosition = goalPos
                    });
                    ecb.AddComponent<SendRpcCommandRequest>(rpcEntity);
                }
                else
                {
                    CreateMoveRpc(ecb, unitGhostId, goalPos);
                }
            }
        }

        private void CreateMoveRpc(EntityCommandBuffer ecb, int unitGhostId, float3 position)
        {
            var rpcEntity = ecb.CreateEntity();
            ecb.AddComponent(rpcEntity, new MoveRequestRpc
            {
                UnitGhostId = unitGhostId,
                TargetPosition = position
            });
            ecb.AddComponent<SendRpcCommandRequest>(rpcEntity);
        }
    }
}