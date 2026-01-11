using Unity.Burst;
using Unity.Entities;
using Unity.NetCode;
using Unity.Collections;
using Unity.Mathematics;

namespace Shared
{
    [BurstCompile]
    [UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
    partial struct CommandProcessingSystem : ISystem
    {
        // 다른 엔티티 정보 확인하기 위한 Lookup들
        [ReadOnly] private ComponentLookup<Team> _teamLookup;
        [ReadOnly] private ComponentLookup<UnitTag> _unitTagLookup;
        [ReadOnly] private ComponentLookup<EnemyTag> _enemyTagLookup;
        [ReadOnly] private ComponentLookup<StructureTag> _structureTagLookup;
        [ReadOnly] private ComponentLookup<ResourceNodeTag> _resourceNodeTagLookup;
        [ReadOnly] private ComponentLookup<WorkerTag> _workerTagLookup;
        [ReadOnly] private ComponentLookup<ResourceCenterTag> _resourceCenterTagLookup;
        [ReadOnly] private ComponentLookup<WorkerState> _workerStateLookup;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkTime>();
            state.RequireForUpdate<GhostIdMap>();
            _teamLookup = state.GetComponentLookup<Team>(true);
            _unitTagLookup = state.GetComponentLookup<UnitTag>(true);
            _enemyTagLookup = state.GetComponentLookup<EnemyTag>(true);
            _structureTagLookup = state.GetComponentLookup<StructureTag>(true);
            _resourceNodeTagLookup = state.GetComponentLookup<ResourceNodeTag>(true);
            _workerTagLookup = state.GetComponentLookup<WorkerTag>(true);
            _resourceCenterTagLookup = state.GetComponentLookup<ResourceCenterTag>(true);
            _workerStateLookup = state.GetComponentLookup<WorkerState>(true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var networkTime = SystemAPI.GetSingleton<NetworkTime>();

            if (!SystemAPI.TryGetSingleton<GhostIdMap>(out var ghostIdMapData))
                return;

            var ghostiDMap = ghostIdMapData.Map;

            _teamLookup.Update(ref state);
            _unitTagLookup.Update(ref state);
            _enemyTagLookup.Update(ref state);
            _structureTagLookup.Update(ref state);
            _resourceNodeTagLookup.Update(ref state);
            _workerTagLookup.Update(ref state);
            _resourceCenterTagLookup.Update(ref state);
            _workerStateLookup.Update(ref state);

            // [중요] EnabledRefRW를 사용하되, 비활성화 상태도 쿼리하기 위해 IgnoreComponentEnabledState 사용
            foreach (var (inputBuffer, movementGoal, unitIntentState, aggroTarget, waypointsEnabled, commandedEntity) in
                     SystemAPI.Query<
                             DynamicBuffer<UnitCommand>,
                             RefRW<MovementGoal>,
                             RefRW<UnitIntentState>,
                             RefRW<AggroTarget>,
                             EnabledRefRW<MovementWaypoints>>()
                         .WithAll<Simulate>()
                         .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                         .WithEntityAccess())
            {
                if (!inputBuffer.GetDataAtTick(networkTime.ServerTick, out var inputCommand))
                {
                    // 명령이 없으면 스킵
                    continue;
                }

                // 빈 명령이면 스킵 (이전 명령 반복 방지)
                if (inputCommand.CommandType == UnitCommandType.None)
                {
                    continue;
                }

                // ==========================================================
                // 우클릭 (Command) 처리 로직
                // ==========================================================
                if (inputCommand.CommandType == UnitCommandType.RightClick)
                {
                    // 1. 타겟 엔티티 찾기
                    bool hasTargetEntity = inputCommand.TargetGhostId != 0;
                    Entity targetEntity = Entity.Null;
                    if (hasTargetEntity)
                    {
                        if (!ghostiDMap.TryGetValue(inputCommand.TargetGhostId, out targetEntity))
                        {
                            hasTargetEntity = false; // 타겟 소실
                        }
                    }

                    if (!hasTargetEntity)
                    {
                        // ------------------------------------------------------
                        // A. 땅을 클릭한 경우 (타겟 없음) -> 무조건 이동
                        // ------------------------------------------------------
                        if (math.distance(movementGoal.ValueRW.Destination, inputCommand.GoalPosition) > 0.1f)
                        {
                            SetUnitMovement(ref movementGoal.ValueRW, inputCommand.GoalPosition, waypointsEnabled);
                            SetUnitIntentState(ref unitIntentState.ValueRW, Intent.Move, ref targetEntity); // targetEntity는 Null
                        }
                    }
                    else
                    {
                        // ------------------------------------------------------
                        // B. 무언가를 클릭한 경우 (스마트 판단)
                        // ------------------------------------------------------

                        // Case 1: 자원 노드 클릭 (Gather)
                        if (_resourceNodeTagLookup.HasComponent(targetEntity))
                        {
                            // Worker만 Gather Intent 설정
                            if (_workerTagLookup.HasComponent(commandedEntity))
                            {
                                SetUnitIntentState(ref unitIntentState.ValueRW, Intent.Gather, ref targetEntity);
                                // MovementGoal은 서버가 HandleGatherRequestSystem에서 설정
                            }
                            else
                            {
                                // Worker가 아니면 이동으로 처리
                                if (math.distance(movementGoal.ValueRW.Destination, inputCommand.GoalPosition) > 0.1f)
                                {
                                    SetUnitMovement(ref movementGoal.ValueRW, inputCommand.GoalPosition, waypointsEnabled);
                                    SetUnitIntentState(ref unitIntentState.ValueRW, Intent.Move, ref targetEntity);
                                }
                            }
                        }
                        // Case 2: 적 클릭 (Attack)
                        else if (_enemyTagLookup.HasComponent(targetEntity))
                        {
                            // 공격 명령 설정
                            SetUnitIntentState(ref unitIntentState.ValueRW, Intent.Attack, ref targetEntity);

                            // AggroTarget 설정 (적과 동일한 타겟팅 시스템 공유)
                            aggroTarget.ValueRW.TargetEntity = targetEntity;
                            aggroTarget.ValueRW.LastTargetPosition = inputCommand.GoalPosition;

                            // 타겟 위치로 이동 (추격)
                            if (math.distance(movementGoal.ValueRW.Destination, inputCommand.GoalPosition) > 0.1f)
                            {
                                SetUnitMovement(ref movementGoal.ValueRW, inputCommand.GoalPosition, waypointsEnabled);
                            }
                        }
                        // Case 3: 리소스 센터 클릭 (자원 반납)
                        else if (_resourceCenterTagLookup.HasComponent(targetEntity))
                        {
                            // Worker가 자원을 들고 있으면 Intent.Gather 유지 (RPC가 Phase.MovingToReturn 설정)
                            if (_workerTagLookup.HasComponent(commandedEntity) &&
                                _workerStateLookup.TryGetComponent(commandedEntity, out var workerState) &&
                                workerState.CarriedAmount > 0)
                            {
                                // Intent.Gather로 설정 (HandleReturnResourceRequestSystem과 일관성 유지)
                                SetUnitIntentState(ref unitIntentState.ValueRW, Intent.Gather, ref targetEntity);
                                // MovementGoal은 HandleReturnResourceRequestSystem에서 설정
                            }
                            else
                            {
                                // 자원이 없으면 일반 이동으로 처리
                                if (math.distance(movementGoal.ValueRW.Destination, inputCommand.GoalPosition) > 0.1f)
                                {
                                    SetUnitMovement(ref movementGoal.ValueRW, inputCommand.GoalPosition, waypointsEnabled);
                                    Entity nullEntity = Entity.Null;
                                    SetUnitIntentState(ref unitIntentState.ValueRW, Intent.Move, ref nullEntity);
                                }
                            }
                        }
                        // Case 4: 기타 (이동)
                        else
                        {
                            if (math.distance(movementGoal.ValueRW.Destination, inputCommand.GoalPosition) > 0.1f)
                            {
                                SetUnitMovement(ref movementGoal.ValueRW, inputCommand.GoalPosition, waypointsEnabled);
                                Entity nullEntity = Entity.Null;
                                SetUnitIntentState(ref unitIntentState.ValueRW, Intent.Move, ref nullEntity);
                            }
                        }
                    }

                }
                // ==========================================================
                // 건설키 (BuildKey) 처리 로직
                // ==========================================================
                else if (inputCommand.CommandType == UnitCommandType.BuildKey)
                {
                    // 건설 위치로 이동 (이동 목표가 변경되었을 때만)
                    if (math.distance(movementGoal.ValueRW.Destination, inputCommand.GoalPosition) > 0.1f)
                    {
                        SetUnitMovement(ref movementGoal.ValueRW, inputCommand.GoalPosition, waypointsEnabled);
                        Entity nullEntity = Entity.Null;
                        SetUnitIntentState(ref unitIntentState.ValueRW, Intent.Build, ref nullEntity);
                    }
                }
            }
        }

        private void SetUnitMovement(ref MovementGoal goal, float3 position, EnabledRefRW<MovementWaypoints> enabledState)
        {
            goal.Destination = position;
            goal.IsPathDirty = true;
            goal.CurrentWaypointIndex = 0;
            // 주의: PathfindingSystem이 경로 계산 후 활성화하므로 여기서는 활성화하지 않음
            // enabledState.ValueRW = true;
        }

        // 코드 중복 방지를 위한 헬퍼 함수
        private void SetUnitIntentState(ref UnitIntentState intentState, Intent intent, ref Entity target)
        {
            intentState.State = intent;
            intentState.TargetEntity = target;
        }
    }
}
