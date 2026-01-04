using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using Shared;

namespace Client
{
    /// <summary>
    /// 사용자가 유닛에게 명령을 입력하는 시스템
    /// - 우클릭 → 이동 명령 (다중 유닛 분산 도착 지원)
    /// - 우클릭 ResourceNode → 채집 명령 (Worker만)
    /// - (향후) A-클릭 → 공격 명령, S → 정지 명령 등
    /// </summary>
    [UpdateInGroup(typeof(GhostInputSystemGroup))]
    [UpdateAfter(typeof(SelectionStateSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct UnitCommandInputSystem : ISystem
    {
        private ComponentLookup<PendingBuildRequest> _pendingBuildLookup;
        private ComponentLookup<UnitState> _unitStateLookup;
        private ComponentLookup<WorkerTag> _workerTagLookup;
        private ComponentLookup<GhostInstance> _ghostInstanceLookup;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkStreamInGame>();
            state.RequireForUpdate<NetworkId>();
            state.RequireForUpdate<UserState>();
            state.RequireForUpdate<BeginSimulationEntityCommandBufferSystem.Singleton>();

            _pendingBuildLookup = state.GetComponentLookup<PendingBuildRequest>(true);
            _unitStateLookup = state.GetComponentLookup<UnitState>(true);
            _workerTagLookup = state.GetComponentLookup<WorkerTag>(true);
            _ghostInstanceLookup = state.GetComponentLookup<GhostInstance>(true);
        }

        public void OnUpdate(ref SystemState state)
        {
            var userState = SystemAPI.GetSingleton<UserState>();
            if (userState.CurrentState == UserContext.Dead) return;

            _pendingBuildLookup.Update(ref state);
            _unitStateLookup.Update(ref state);
            _workerTagLookup.Update(ref state);
            _ghostInstanceLookup.Update(ref state);

            ProcessRightClickCommand(ref state);
            SubmitCommands(ref state);
        }

        /// <summary>
        /// 우클릭 입력 처리 → RTSInputState 갱신 + 분산 도착 또는 채집 명령
        /// </summary>
        private void ProcessRightClickCommand(ref SystemState state)
        {
            var mouse = Mouse.current;
            if (mouse == default || !mouse.rightButton.wasPressedThisFrame) return;
            if (!Camera.main) return;

            float2 mousePos = mouse.position.ReadValue();
            UnityEngine.Ray ray = Camera.main.ScreenPointToRay(new Vector3(mousePos.x, mousePos.y, 0));

            int groundMask = 1 << 3;        // Ground
            int resourceNodeMask = 1 << 6;  // ResourceNode 레이어 (필요시 수정)

            var ecb = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged);

            // 먼저 ResourceNode 클릭 여부 확인
            if (Physics.Raycast(ray, out UnityEngine.RaycastHit resourceHit, 1000f, resourceNodeMask))
            {
                Debug.Log("자원 클릭함");
                // ResourceNode를 클릭한 경우 - 채집 명령 처리
                ProcessGatherCommand(ref state, resourceHit, ecb);
                return;
            }

            // Ground 클릭 - 이동 명령 처리
            if (Physics.Raycast(ray, out UnityEngine.RaycastHit hit, 1000f, groundMask))
            {
                ProcessMoveCommand(ref state, hit.point, ecb);
            }
        }

        /// <summary>
        /// 채집 명령 처리 - 선택된 Worker들에게 ResourceNode 채집 명령
        /// </summary>
        private void ProcessGatherCommand(ref SystemState state, RaycastHit resourceHit, EntityCommandBuffer ecb)
        {
            // ResourceNode의 Collider에서 Entity 찾기
            var resourceNodeGameObject = resourceHit.collider.gameObject;
            Entity resourceNodeEntity = Entity.Null;
            int resourceNodeGhostId = 0;

            // EntityManager를 통해 ResourceNodeTag를 가진 엔티티 중 위치가 일치하는 것 찾기
            float3 hitPos = resourceHit.point;
            float closestDist = float.MaxValue;

            foreach (var (transform, ghostInstance, entity) in SystemAPI.Query<RefRO<Unity.Transforms.LocalTransform>, RefRO<GhostInstance>>()
                .WithAll<ResourceNodeTag>()
                .WithEntityAccess())
            {
                float dist = math.distance(transform.ValueRO.Position, hitPos);
                if (dist < closestDist && dist < 5f) // 5 unit 이내
                {
                    closestDist = dist;
                    resourceNodeEntity = entity;
                    resourceNodeGhostId = ghostInstance.ValueRO.ghostId;
                }
            }

            if (resourceNodeEntity == Entity.Null) return;

            // 선택된 Worker들에게 채집 RPC 전송
            foreach (var (inputState, ghostInstance, entity) in SystemAPI.Query<RefRW<RTSInputState>, RefRO<GhostInstance>>() // RefRO -> RefRW 변경
                         .WithAll<Selected, GhostOwnerIsLocal, WorkerTag>()
                         .WithEntityAccess())
            {
                // 1. RPC 전송
                var rpcEntity = ecb.CreateEntity();
                ecb.AddComponent(rpcEntity, new GatherRequestRpc
                {
                    WorkerGhostId = ghostInstance.ValueRO.ghostId,
                    ResourceNodeGhostId = resourceNodeGhostId,
                    ReturnPointGhostId = 0
                });
                ecb.AddComponent(rpcEntity, new SendRpcCommandRequest());

                // 2. [중요] 기존 이동 명령 취소
                // 이걸 안 하면 다음 프레임에 Move Command가 전송되어 서버의 상태 변경을 덮어쓸 수 있습니다.
                inputState.ValueRW.HasTarget = false; 

                // 3. PendingBuildRequest 취소
                if (_pendingBuildLookup.HasComponent(entity))
                {
                    ecb.RemoveComponent<PendingBuildRequest>(entity);
                }
            }
        }

        /// <summary>
        /// 이동 명령 처리 - 선택된 유닛들에게 분산 이동 명령
        /// </summary>
        private void ProcessMoveCommand(ref SystemState state, float3 centerTargetPos, EntityCommandBuffer ecb)
        {
            // 1. 선택된 유닛 수 카운트 + 엔티티 목록 수집
            var selectedUnits = new NativeList<Entity>(16, Allocator.Temp);
            foreach (var (_, entity) in SystemAPI.Query<RefRO<RTSInputState>>()
                .WithAll<Selected, GhostOwnerIsLocal>()
                .WithEntityAccess())
            {
                selectedUnits.Add(entity);
            }

            int totalUnits = selectedUnits.Length;

            // 2. 각 유닛에 분산된 목표 위치 할당
            for (int i = 0; i < totalUnits; i++)
            {
                Entity entity = selectedUnits[i];

                // 분산 도착 위치 계산
                float3 formationPos = FormationUtility.CalculateFormationPosition(
                    centerTargetPos, i, totalUnits);

                // RTSInputState 갱신
                if (SystemAPI.HasComponent<RTSInputState>(entity))
                {
                    var inputState = SystemAPI.GetComponentRW<RTSInputState>(entity);
                    inputState.ValueRW.TargetPosition = formationPos;
                    inputState.ValueRW.HasTarget = true;
                }

                // PendingBuildRequest가 있으면 취소 (이동 후 건설 취소)
                if (_pendingBuildLookup.HasComponent(entity))
                {
                    ecb.RemoveComponent<PendingBuildRequest>(entity);
                }

                // MovingToBuild 또는 채집 관련 상태이면 Moving으로 변경
                if (_unitStateLookup.HasComponent(entity))
                {
                    var currentState = _unitStateLookup[entity].CurrentState;
                    if (currentState == UnitContext.MovingToBuild ||
                        currentState == UnitContext.MovingToGather ||
                        currentState == UnitContext.MovingToReturn ||
                        currentState == UnitContext.Gathering)
                    {
                        ecb.SetComponent(entity, new UnitState
                        {
                            CurrentState = UnitContext.Moving
                        });
                    }
                }
            }

            selectedUnits.Dispose();
        }

        /// <summary>
        /// RTSInputState → RTSCommand 버퍼에 명령 제출
        /// </summary>
        private void SubmitCommands(ref SystemState state)
        {
            var networkTime = SystemAPI.GetSingleton<NetworkTime>();
            NetworkTick tick = networkTime.ServerTick;

            foreach (var (inputState, inputBuffer) in SystemAPI.Query<RefRO<RTSInputState>, DynamicBuffer<RTSCommand>>()
                .WithAll<GhostOwnerIsLocal>())
            {
                var command = new RTSCommand
                {
                    Tick = tick,
                    TargetPosition = inputState.ValueRO.TargetPosition,
                    TargetGhostId = 0, // 향후 공격 명령에서 사용
                    CommandType = inputState.ValueRO.HasTarget ? RTSCommandType.Move : RTSCommandType.None
                };

                inputBuffer.AddCommandData(command);
            }
        }
    }
}