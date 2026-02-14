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
    /// 유닛 명령 입력 처리 (우클릭: 이동/공격/채집/반납, A+좌클릭: AttackMove)
    /// - 선택된 유닛에 대해 RPC 전송 + 커맨드 마커 활성화 (풀링)
    /// </summary>
    [UpdateInGroup(typeof(GhostInputSystemGroup))]
    [UpdateAfter(typeof(SelectedEntityInfoUpdateSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial class UnitCommandInputSystem : SystemBase
    {
        private ComponentLookup<GhostInstance> _ghostInstanceLookup;
        private ComponentLookup<ResourceNodeTag> _resourceNodeTagLookup;
        private ComponentLookup<WorkerTag> _workerTagLookup;
        private ComponentLookup<ResourceCenterTag> _resourceCenterTagLookup;
        private ComponentLookup<WorkerState> _workerStateLookup;
        private ComponentLookup<EnemyTag> _enemyTagLookup;

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
            var keyboard = Keyboard.current;
            if (mouse == null) return;

            // A+좌클릭 = AttackMove, 우클릭 = 일반 명령
            bool isRightClick = mouse.rightButton.wasPressedThisFrame;
            bool isAttackMoveClick = keyboard != null && keyboard.aKey.isPressed && mouse.leftButton.wasPressedThisFrame;

            if (!isRightClick && !isAttackMoveClick)
                return;

            if (_cachedCamera == null)
            {
                _cachedCamera = Camera.main;
                if (_cachedCamera == null) return;
            }

            _ghostInstanceLookup.Update(this);
            _resourceNodeTagLookup.Update(this);
            _workerTagLookup.Update(this);
            _resourceCenterTagLookup.Update(this);
            _workerStateLookup.Update(this);
            _enemyTagLookup.Update(this);

            ProcessMouseInput(mouse, isAttackMoveClick);
        }

        private void ProcessMouseInput(Mouse mouse, bool isAttackMove)
        {
            float2 mousePos = mouse.position.ReadValue();
            UnityEngine.Ray unityRay = _cachedCamera.ScreenPointToRay(new Vector3(mousePos.x, mousePos.y, 0));

            var physicsWorldSingleton = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
            var collisionWorld = physicsWorldSingleton.CollisionWorld;

            var rayInput = new RaycastInput
            {
                Start = unityRay.origin,
                End = unityRay.origin + unityRay.direction * 1000f,
                Filter = new CollisionFilter
                {
                    BelongsTo = ~0u,
                    CollidesWith = ~0u,
                    GroupIndex = 0
                }
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

                SendCommandToSelectedUnits(hit.Position, targetGhostId, isResourceNode, isResourceCenter, isEnemy, isAttackMove);
            }
        }

        private void SendCommandToSelectedUnits(
            float3 goalPos,
            int targetGhostId,
            bool isResourceNode,
            bool isResourceCenter,
            bool isEnemy,
            bool isAttackMove)
        {
            var ecb = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(World.Unmanaged);

            // 마커 타입 추적 (0=None, 1=Move, 2=Gather, 3=Attack) - 우선순위: Attack > Gather > Move
            int markerType = 0;

            foreach (var (_, entity) in SystemAPI.Query<RefRO<UnitTag>>()
                .WithAll<Selected, GhostOwnerIsLocal>()
                .WithEntityAccess())
            {
                if (!_ghostInstanceLookup.TryGetComponent(entity, out var unitGhost))
                    continue;

                int unitGhostId = unitGhost.ghostId;
                bool isWorker = _workerTagLookup.HasComponent(entity);

                // AttackMove가 아닌 경우에만 Worker 특수 명령 처리
                if (!isAttackMove)
                {
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
                        markerType = math.max(markerType, 2);
                        continue;
                    }

                    if (isResourceCenter && isWorker)
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
                            markerType = math.max(markerType, 2);
                            continue;
                        }
                    }
                }

                // 적 클릭 시 공격 명령 (AttackMove 또는 우클릭 모두)
                if (isEnemy && targetGhostId != 0)
                {
                    var rpcEntity = ecb.CreateEntity();
                    ecb.AddComponent(rpcEntity, new AttackRequestRpc
                    {
                        UnitGhostId = unitGhostId,
                        TargetGhostId = targetGhostId,
                        TargetPosition = goalPos
                    });
                    ecb.AddComponent<SendRpcCommandRequest>(rpcEntity);
                    markerType = math.max(markerType, 3);
                }
                else
                {
                    // 땅 클릭: 이동 (AttackMove 여부에 따라 Intent 결정)
                    CreateMoveRpc(ecb, unitGhostId, goalPos, isAttackMove);
                    markerType = math.max(markerType, isAttackMove ? 3 : 1);
                }
            }

            if (markerType > 0)
                ActivatePooledMarker(goalPos, markerType);
        }

        private void ActivatePooledMarker(float3 position, int commandType)
        {
            byte targetType = (byte)commandType;
            RefRW<CommandMarkerLifetime> bestLifetime = default;
            RefRW<LocalTransform> bestTransform = default;
            float lowestRemaining = float.MaxValue;
            bool found = false;

            foreach (var (lifetime, transform) in
                SystemAPI.Query<RefRW<CommandMarkerLifetime>, RefRW<LocalTransform>>()
                    .WithAll<CommandMarkerTag>())
            {
                if (lifetime.ValueRO.MarkerType != targetType)
                    continue;

                if (lifetime.ValueRO.RemainingTime < lowestRemaining)
                {
                    lowestRemaining = lifetime.ValueRO.RemainingTime;
                    bestLifetime = lifetime;
                    bestTransform = transform;
                    found = true;
                }
            }

            if (!found) return;

            bestLifetime.ValueRW.TotalTime = 1.0f;
            bestLifetime.ValueRW.RemainingTime = 1.0f;
            bestLifetime.ValueRW.InitialScale = 2.0f;

            bestTransform.ValueRW.Position = new float3(position.x, 0.05f, position.z);
            bestTransform.ValueRW.Rotation = quaternion.Euler(math.radians(90f), 0f, 0f);
            bestTransform.ValueRW.Scale = 2.0f;
        }

        private void CreateMoveRpc(EntityCommandBuffer ecb, int unitGhostId, float3 position, bool isAttackMove)
        {
            var rpcEntity = ecb.CreateEntity();
            ecb.AddComponent(rpcEntity, new MoveRequestRpc
            {
                UnitGhostId = unitGhostId,
                TargetPosition = position,
                IsAttackMove = isAttackMove
            });
            ecb.AddComponent<SendRpcCommandRequest>(rpcEntity);
        }
    }
}