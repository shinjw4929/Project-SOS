using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using Shared;

namespace Server
{
    /// <summary>
    /// 건물 파괴 시 NavMeshObstacle GameObject 제거 + 주변 Partial Path 경로 무효화 (Cleanup 패턴 적용)
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ServerDeathSystem))] // 죽음 처리 "이후"에 실행되어야 잔해를 치움
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial class NavMeshObstacleCleanupSystem : SystemBase
    {
        private const float PartialPathInvalidationRadius = 12f;

        protected override void OnUpdate()
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            // 쿼리 논리:
            // 1. NavMeshObstacleReference(Cleanup)는 가지고 있는데
            // 2. StructureTag, ResourceNodeTag 모두 없는 엔티티
            // => "DestroyEntity가 호출되어 본체는 날아갔지만, 아직 뒷수습이 필요한 상태"
            foreach (var (obstacleRef, entity) in
                     SystemAPI.Query<NavMeshObstacleReference>()
                         .WithNone<StructureTag, ResourceNodeTag>()
                         .WithEntityAccess())
            {
                // 1. GameObject 파괴 전에 위치 획득 + 주변 Partial Path 무효화
                if (obstacleRef.ObstacleObject != null)
                {
                    InvalidateNearbyPartialPaths(
                        (float3)obstacleRef.ObstacleObject.transform.position);
                    UnityEngine.Object.Destroy(obstacleRef.ObstacleObject);
                }

                // 2. Cleanup 컴포넌트 제거 (이제 엔티티가 완전히 소멸됨)
                ecb.RemoveComponent<NavMeshObstacleReference>(entity);
            }

            ecb.Playback(EntityManager);
            ecb.Dispose();
        }

        private void InvalidateNearbyPartialPaths(float3 buildingPos)
        {
            float radiusSq = PartialPathInvalidationRadius * PartialPathInvalidationRadius;

            // EnemyTag: Dormant 깨우기 + Partial Path 무효화
            foreach (var (goal, transform, enemyState) in
                     SystemAPI.Query<RefRW<MovementGoal>, RefRO<LocalTransform>, RefRW<EnemyState>>()
                         .WithAll<EnemyTag>())
            {
                float3 entityPos = transform.ValueRO.Position;
                if (math.distancesq(entityPos, buildingPos) >= radiusSq) continue;

                if (enemyState.ValueRO.CurrentState == EnemyContext.Dormant)
                    enemyState.ValueRW.CurrentState = EnemyContext.Idle;
                else if (goal.ValueRO.IsPathPartial)
                    goal.ValueRW.IsPathDirty = true;
            }

            // UnitTag: Partial Path 무효화만
            foreach (var (goal, transform) in
                     SystemAPI.Query<RefRW<MovementGoal>, RefRO<LocalTransform>>()
                         .WithAll<UnitTag>())
            {
                if (!goal.ValueRO.IsPathPartial) continue;
                if (math.distancesq(transform.ValueRO.Position, buildingPos) < radiusSq)
                    goal.ValueRW.IsPathDirty = true;
            }
        }
    }
}