using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using Shared;
using Unity.Collections;

namespace Client
{
    /// <summary>
    /// PendingBuildRequest를 가진 유닛이 사거리 내 도착 시 BuildRequestRpc 전송
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(GhostInputSystemGroup))]
    [UpdateAfter(typeof(StructurePlacementInputSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct PendingBuildExecuteSystem : ISystem
    {
        private ComponentLookup<MovementDestination> _moveTargetLookup;
        private ComponentLookup<ObstacleRadius> _obstacleRadiusLookup;
        [ReadOnly] private ComponentLookup<GhostInstance> _ghostInstanceLookup;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkStreamInGame>();
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();

            _moveTargetLookup = state.GetComponentLookup<MovementDestination>(false);
            _obstacleRadiusLookup = state.GetComponentLookup<ObstacleRadius>(true);
            _ghostInstanceLookup = state.GetComponentLookup<GhostInstance>(true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            _moveTargetLookup.Update(ref state);
            _obstacleRadiusLookup.Update(ref state);
            _ghostInstanceLookup.Update(ref state);

            var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged);

            foreach (var (transform, pending, unitState, entity) in
                     SystemAPI.Query<RefRO<LocalTransform>, RefRO<PendingBuildRequest>, RefRW<UnitState>>()
                         .WithAll<GhostOwnerIsLocal, BuilderTag>()
                         .WithEntityAccess())
            {
                float3 unitPos = transform.ValueRO.Position;
                float3 buildCenter = pending.ValueRO.BuildSiteCenter;
                float structureRadius = pending.ValueRO.StructureRadius;

                // 유닛 반지름 조회
                float unitRadius = 0.5f; // 기본값
                if (_obstacleRadiusLookup.HasComponent(entity))
                {
                    unitRadius = _obstacleRadiusLookup[entity].Radius;
                }

                // 중심점 거리 계산 (XZ 평면) - 건물 반지름 빼기
                float centerDistance = math.distance(
                    new float2(unitPos.x, unitPos.z),
                    new float2(buildCenter.x, buildCenter.z)
                );
                float distanceToSurface = centerDistance - structureRadius;

                // 도착 판정: 유닛 반지름 + 이동 시스템 오차(0.5f) + 여유분 이내면 도착
                // NetcodeUnitMovementSystem.ArrivalThreshold = 0.5f를 고려
                float arrivalThreshold = unitRadius + 0.5f + 0.5f;
                if (distanceToSurface <= arrivalThreshold)
                {
                    // Builder의 GhostId 조회
                    int builderGhostId = 0;
                    if (_ghostInstanceLookup.HasComponent(entity))
                    {
                        builderGhostId = _ghostInstanceLookup[entity].ghostId;
                    }

                    // 건설 RPC 전송 (BuilderGhostId 포함)
                    var rpcEntity = ecb.CreateEntity();
                    ecb.AddComponent(rpcEntity, new BuildRequestRpc
                    {
                        StructureIndex = pending.ValueRO.StructureIndex,
                        GridPosition = pending.ValueRO.GridPosition,
                        BuilderGhostId = builderGhostId
                    });
                    ecb.AddComponent<SendRpcCommandRequest>(rpcEntity);

                    // PendingBuildRequest 제거
                    ecb.RemoveComponent<PendingBuildRequest>(entity);

                    // 유닛 상태 복원: Idle
                    unitState.ValueRW.CurrentState = UnitContext.Idle;

                    // 이동 중지 (ComponentLookup으로 안전하게 접근)
                    if (_moveTargetLookup.HasComponent(entity))
                    {
                        _moveTargetLookup[entity] = new MovementDestination
                        {
                            Position = float3.zero,
                            IsValid = false
                        };
                    }
                }
            }
        }
    }
}
