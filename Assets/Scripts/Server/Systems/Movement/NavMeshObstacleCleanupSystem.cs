using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using Shared;

namespace Server
{
    /// <summary>
    /// 건물 파괴 시 NavMeshObstacle GameObject 제거
    /// - NavMeshObstacleReference (Managed Component) 사용
    /// - FindObjectsOfType 제거 → O(1) 접근
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(ServerDeathSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial class NavMeshObstacleCleanupSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            foreach (var (health, obstacleRef, entity) in
                SystemAPI.Query<RefRO<Health>, NavMeshObstacleReference>()
                    .WithAll<StructureTag>()
                    .WithEntityAccess())
            {
                if (health.ValueRO.CurrentValue <= 0 && obstacleRef.ObstacleObject != null)
                {
                    Object.Destroy(obstacleRef.ObstacleObject);
                    ecb.RemoveComponent<NavMeshObstacleReference>(entity);
                }
            }

            ecb.Playback(EntityManager);
            ecb.Dispose();
        }
    }
}
