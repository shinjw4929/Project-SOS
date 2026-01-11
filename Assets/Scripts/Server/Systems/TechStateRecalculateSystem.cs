using Unity.Burst;
using Unity.Entities;
using Unity.Collections;
using Unity.NetCode;
using Shared;

namespace Server
{
    /// <summary>
    /// [Server 전용] ResourceCenter 파괴 시 유저별 테크 상태 재계산
    /// ServerDeathSystem 이후에 실행되어, 파괴된 ResourceCenter를 감지하고 테크 상태 업데이트
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ServerDeathSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [BurstCompile]
    public partial struct TechStateRecalculateSystem : ISystem
    {
        private EntityQuery _resourceCenterQuery;

        public void OnCreate(ref SystemState state)
        {
            // ResourceCenter 쿼리 (파괴 예정인 것 감지용)
            _resourceCenterQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<ResourceCenterTag>(),
                ComponentType.ReadOnly<Health>(),
                ComponentType.ReadOnly<GhostOwner>()
            );

            state.RequireForUpdate<UserEconomyTag>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // 파괴 예정인 ResourceCenter가 있는지 확인 (Health <= 0)
            var destroyedOwners = new NativeList<int>(Allocator.Temp);

            foreach (var (health, ghostOwner) in SystemAPI.Query<RefRO<Health>, RefRO<GhostOwner>>()
                         .WithAll<ResourceCenterTag>())
            {
                if (health.ValueRO.CurrentValue <= 0)
                {
                    // 이 유저의 ResourceCenter가 파괴됨
                    destroyedOwners.Add(ghostOwner.ValueRO.NetworkId);
                }
            }

            if (destroyedOwners.Length == 0)
            {
                destroyedOwners.Dispose();
                return;
            }

            // 파괴된 유저들에 대해, 다른 살아있는 ResourceCenter가 있는지 확인
            var aliveOwners = new NativeHashSet<int>(16, Allocator.Temp);

            foreach (var (health, ghostOwner) in SystemAPI.Query<RefRO<Health>, RefRO<GhostOwner>>()
                         .WithAll<ResourceCenterTag>())
            {
                if (health.ValueRO.CurrentValue > 0)
                {
                    aliveOwners.Add(ghostOwner.ValueRO.NetworkId);
                }
            }

            // 유저별 테크 상태 업데이트
            foreach (var (techState, ghostOwner) in SystemAPI.Query<RefRW<UserTechState>, RefRO<GhostOwner>>()
                         .WithAll<UserEconomyTag>())
            {
                int networkId = ghostOwner.ValueRO.NetworkId;

                // 이 유저의 ResourceCenter가 파괴되었고, 다른 살아있는 것이 없으면 해금 취소
                bool wasDestroyed = false;
                for (int i = 0; i < destroyedOwners.Length; i++)
                {
                    if (destroyedOwners[i] == networkId)
                    {
                        wasDestroyed = true;
                        break;
                    }
                }

                if (wasDestroyed && !aliveOwners.Contains(networkId))
                {
                    techState.ValueRW.HasResourceCenter = false;
                }
            }

            destroyedOwners.Dispose();
            aliveOwners.Dispose();
        }
    }
}
