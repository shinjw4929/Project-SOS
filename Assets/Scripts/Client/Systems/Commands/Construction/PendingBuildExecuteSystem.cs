using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using Shared;

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
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkStreamInGame>();
            state.RequireForUpdate<BeginSimulationEntityCommandBufferSystem.Singleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var ecb = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged);

            foreach (var (transform, pending, unitState, entity) in
                     SystemAPI.Query<RefRO<LocalTransform>, RefRO<PendingBuildRequest>, RefRW<UnitState>>()
                         .WithAll<GhostOwnerIsLocal, BuilderTag>()
                         .WithEntityAccess())
            {
                float3 unitPos = transform.ValueRO.Position;
                float3 buildCenter = pending.ValueRO.BuildSiteCenter;
                float requiredRange = pending.ValueRO.RequiredRange;

                // AABB 최근접점까지의 거리 계산 (간략화: 중심점 거리)
                // 실제로는 AABB 계산이 필요하지만, 건물 크기를 알 수 없으므로 중심점 기준
                float distance = math.distance(
                    new float2(unitPos.x, unitPos.z),
                    new float2(buildCenter.x, buildCenter.z)
                );

                // 사거리 + 여유분 내에 도착했는지 확인
                if (distance <= requiredRange + 0.5f)
                {
                    // 건설 RPC 전송
                    var rpcEntity = ecb.CreateEntity();
                    ecb.AddComponent(rpcEntity, new BuildRequestRpc
                    {
                        StructureIndex = pending.ValueRO.StructureIndex,
                        GridPosition = pending.ValueRO.GridPosition
                    });
                    ecb.AddComponent<SendRpcCommandRequest>(rpcEntity);

                    // PendingBuildRequest 제거
                    ecb.RemoveComponent<PendingBuildRequest>(entity);

                    // 유닛 상태 복원: Idle
                    unitState.ValueRW.CurrentState = UnitContext.Idle;

                    // 이동 중지
                    if (SystemAPI.HasComponent<MoveTarget>(entity))
                    {
                        ecb.SetComponent(entity, new MoveTarget
                        {
                            position = float3.zero,
                            isValid = false
                        });
                    }
                }
            }
        }
    }
}
