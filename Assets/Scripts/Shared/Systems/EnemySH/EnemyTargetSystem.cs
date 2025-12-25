using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Shared;

// Enemy가 Player 컴포넌트를 가진 Entity를 찾아
// 가장 가까운 플레이어를 타겟으로 설정하는 시스템
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
public partial struct EnemyTargetSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (enemyTransform, enemyTarget, config) in
            SystemAPI.Query<
                RefRO<LocalTransform>,
                RefRW<EnemyTarget>,
                RefRO<EnemyFollowConfig>>())
        {
            bool needNewTarget = false;

            // 1. 기존 타겟이 없거나
            // 2. 타겟 Entity가 사라졌으면
            if (!enemyTarget.ValueRO.HasTarget ||
                !state.EntityManager.Exists(enemyTarget.ValueRO.TargetEntity))
            {
                needNewTarget = true;
            }
            else
            {
                // 기존 타겟 거리 검사
                var targetTransform =
                    state.EntityManager.GetComponentData<LocalTransform>(
                        enemyTarget.ValueRO.TargetEntity);

                float dist = math.distance(
                    enemyTransform.ValueRO.Position,
                    targetTransform.Position);

                // 너무 멀어지면 타겟 재선정
                if (dist > config.ValueRO.LoseTargetDistance)
                {
                    needNewTarget = true;
                }
                else
                {
                    // 추적 중이면 위치 갱신
                    enemyTarget.ValueRW.LastKnownPosition =
                        targetTransform.Position;
                }
            }

            if (!needNewTarget)
                continue;

            // 가장 가까운 Player 찾기
            Entity closestPlayer = Entity.Null;
            float closestDist = float.MaxValue;

            foreach (var (playerTransform, playerEntity) in
                SystemAPI.Query<RefRO<LocalTransform>>()
                    .WithAll<Player>()
                    .WithEntityAccess())
            {
                float dist = math.distance(
                    enemyTransform.ValueRO.Position,
                    playerTransform.ValueRO.Position);

                if (dist < closestDist)
                {
                    closestDist = dist;
                    closestPlayer = playerEntity;
                }
            }

            // 새 타겟 확정
            if (closestPlayer != Entity.Null)
            {
                var playerTransform =
                    state.EntityManager.GetComponentData<LocalTransform>(
                        closestPlayer);

                enemyTarget.ValueRW.TargetEntity = closestPlayer;
                enemyTarget.ValueRW.HasTarget = true;

                // 중요: 처음 타겟 잡을 때도 반드시 위치 세팅
                enemyTarget.ValueRW.LastKnownPosition =
                    playerTransform.Position;
            }
        }
    }
}
