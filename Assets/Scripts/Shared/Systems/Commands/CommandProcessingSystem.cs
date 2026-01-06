using Unity.Burst;
using Unity.Entities;
using Unity.NetCode;
using Unity.Collections;
using Unity.Mathematics;

namespace Shared
{
    [UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
    [BurstCompile]
    partial struct CommandProcessingSystem : ISystem
    {
        // 다른 엔티티 정보 확인하기 위한 Lookup들
        [ReadOnly] private ComponentLookup<Team> _teamLookup;
        [ReadOnly] private ComponentLookup<UnitTag> _unitTagLookup;
        [ReadOnly] private ComponentLookup<EnemyTag> _enemyTagLookup;
        [ReadOnly] private ComponentLookup<StructureTag> _structureTagLookup;
        [ReadOnly] private ComponentLookup<ResourceNodeTag> _resourceNodeTagLookup;
        
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

            foreach (var (inputBuffer, movementGoal, unitIntentState, waypointsEnabled, commandedEntity) in
                     SystemAPI.Query<
                             DynamicBuffer<UnitCommand>,
                             RefRW<MovementGoal>,
                             RefRW<UnitIntentState>,
                             EnabledRefRW<MovementWaypoints>>()
                         .WithAll<Simulate>()
                         .WithEntityAccess())
            {
                if (!inputBuffer.GetDataAtTick(networkTime.ServerTick, out var inputCommand))
                    continue;

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
                            SetUnitMovement(ref movementGoal.ValueRW, inputCommand.GoalPosition,  waypointsEnabled);
                            SetUnitIntentState(ref unitIntentState.ValueRW, Intent.Move, ref targetEntity); // targetEntity는 Null
                        }
                    }
                    else
                    {
                        // ------------------------------------------------------
                        // B. 무언가를 클릭한 경우 (스마트 판단)
                        // ------------------------------------------------------
                        // TODO 자원, 적, 아군유닛, 건물 등 구현 필요
                    }

                }
            }
            
        }

        private void SetUnitMovement(ref MovementGoal goal, float3 position, EnabledRefRW<MovementWaypoints> enabledState)
        {
            goal.Destination = position;
            goal.IsPathDirty = true;
            goal.CurrentWaypointIndex = 0;
            enabledState.ValueRW = true;
        }
        
        // 코드 중복 방지를 위한 헬퍼 함수
        private void SetUnitIntentState(ref UnitIntentState intentState, Intent intent, ref Entity target)
        {
            intentState.State = intent;
            intentState.TargetEntity = target;
        }
    }
}

