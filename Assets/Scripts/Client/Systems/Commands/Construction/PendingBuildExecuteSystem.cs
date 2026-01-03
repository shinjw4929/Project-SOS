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
        private ComponentLookup<MoveTarget> _moveTargetLookup;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkStreamInGame>();
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
            state.RequireForUpdate<GridSettings>();

            _moveTargetLookup = state.GetComponentLookup<MoveTarget>(false);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            _moveTargetLookup.Update(ref state);

            var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged);

            var gridSettings = SystemAPI.GetSingleton<GridSettings>();

            foreach (var (transform, pending, unitState, entity) in
                     SystemAPI.Query<RefRO<LocalTransform>, RefRO<PendingBuildRequest>, RefRW<UnitState>>()
                         .WithAll<GhostOwnerIsLocal, BuilderTag>()
                         .WithEntityAccess())
            {
                float3 unitPos = transform.ValueRO.Position;
                float3 buildCenter = pending.ValueRO.BuildSiteCenter;
                float requiredRange = pending.ValueRO.RequiredRange;

                // 건물 AABB 계산
                float halfWidth = pending.ValueRO.Width * gridSettings.CellSize * 0.5f;
                float halfLength = pending.ValueRO.Length * gridSettings.CellSize * 0.5f;

                float3 aabbMin = new float3(buildCenter.x - halfWidth, 0, buildCenter.z - halfLength);
                float3 aabbMax = new float3(buildCenter.x + halfWidth, 0, buildCenter.z + halfLength);

                // 유닛 위치에서 AABB 최근접점까지의 거리 계산 (XZ 평면)
                float closestX = math.clamp(unitPos.x, aabbMin.x, aabbMax.x);
                float closestZ = math.clamp(unitPos.z, aabbMin.z, aabbMax.z);

                float dx = unitPos.x - closestX;
                float dz = unitPos.z - closestZ;
                float distance = math.sqrt(dx * dx + dz * dz);

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

                    // 이동 중지 (ComponentLookup으로 안전하게 접근)
                    if (_moveTargetLookup.HasComponent(entity))
                    {
                        _moveTargetLookup[entity] = new MoveTarget
                        {
                            position = float3.zero,
                            isValid = false
                        };
                    }
                }
            }
        }
    }
}
