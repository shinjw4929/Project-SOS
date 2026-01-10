using Shared;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

namespace Client
{
    /// <summary>
    /// 투사체 시각 효과 시스템 (클라이언트)
    /// - ProjectileVisualRpc 수신 → 시각적 투사체 스폰
    /// - 데미지 없음 (서버에서 이미 적용됨)
    /// </summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct ProjectileVisualSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ProjectilePrefabRef>();
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingleton<ProjectilePrefabRef>(out var prefabRef))
                return;

            Entity projectilePrefab = prefabRef.Prefab;
            if (projectilePrefab == Entity.Null)
                return;

            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

            // RPC 처리
            foreach (var (rpc, rpcEntity) in
                     SystemAPI.Query<RefRO<ProjectileVisualRpc>>()
                              .WithAll<ReceiveRpcCommandRequest>()
                              .WithEntityAccess())
            {
                float3 startPos = rpc.ValueRO.StartPosition;
                float3 targetPos = rpc.ValueRO.TargetPosition;

                // 방향 및 거리 계산
                float3 direction = targetPos - startPos;
                float distance = math.length(direction);

                if (distance < 0.1f)
                {
                    ecb.DestroyEntity(rpcEntity);
                    continue;
                }

                direction = math.normalize(direction);

                // 시각적 투사체 스폰
                Entity projectile = ecb.Instantiate(projectilePrefab);

                // 위치 및 회전 설정
                quaternion rotation = quaternion.LookRotationSafe(direction, math.up());
                ecb.SetComponent(projectile, LocalTransform.FromPositionRotationScale(startPos, rotation, 1f));

                // 이동 데이터 설정 (프리팹에 이미 있으므로 SetComponent 사용)
                ecb.SetComponent(projectile, new ProjectileMove
                {
                    Direction = direction,
                    Speed = 30f, // 시각 효과용 빠른 속도
                    RemainingDistance = distance
                });

                // 시각 전용 태그 추가 (데미지 시스템에서 무시하도록)
                ecb.AddComponent(projectile, new VisualOnlyTag());

                // RPC 엔티티 삭제
                ecb.DestroyEntity(rpcEntity);
            }
        }
    }

    /// <summary>
    /// 클라이언트 투사체 이동 시스템
    /// - 시각 효과 투사체 이동 및 삭제
    /// </summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileVisualSystem))]
    public partial struct ClientProjectileMoveSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;

            var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                               .CreateCommandBuffer(state.WorldUnmanaged);

            foreach (var (transform, move, entity) in
                     SystemAPI.Query<RefRW<LocalTransform>, RefRW<ProjectileMove>>()
                              .WithAll<VisualOnlyTag>()
                              .WithNone<Prefab>()
                              .WithEntityAccess())
            {
                ref readonly var moveData = ref move.ValueRO;
                float step = moveData.Speed * dt;

                if (step > moveData.RemainingDistance)
                    step = moveData.RemainingDistance;

                transform.ValueRW.Position += moveData.Direction * step;
                move.ValueRW.RemainingDistance -= step;

                if (move.ValueRO.RemainingDistance <= 0f)
                    ecb.DestroyEntity(entity);
            }
        }
    }

}
