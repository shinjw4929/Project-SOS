using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
public partial struct FireProjectileServerSystem : ISystem
{
    private EntityQuery _rpcQuery;
    private EntityQuery _prefabRefQuery;

    public void OnCreate(ref SystemState state)
    {
        _rpcQuery = state.GetEntityQuery(
            ComponentType.ReadOnly<ReceiveRpcCommandRequest>(),
            ComponentType.ReadOnly<FireProjectileRpc>());

        _prefabRefQuery = state.GetEntityQuery(
            ComponentType.ReadOnly<ProjectilePrefabRef>());
    }

    public void OnUpdate(ref SystemState state)
    {
        var em = state.EntityManager;

        using var rpcEntities = _rpcQuery.ToEntityArray(Allocator.Temp);
        if (rpcEntities.Length == 0)
            return;

        // Projectile prefab 확보
        Entity projectilePrefab = Entity.Null;
        using (var prefEnts = _prefabRefQuery.ToEntityArray(Allocator.Temp))
        {
            if (prefEnts.Length > 0)
                projectilePrefab = em.GetComponentData<ProjectilePrefabRef>(prefEnts[0]).Prefab;
        }

        // 프리팹이 없으면 RPC만 소비
        if (projectilePrefab == Entity.Null || !em.Exists(projectilePrefab))
        {
            for (int i = 0; i < rpcEntities.Length; i++)
                em.DestroyEntity(rpcEntities[i]);
            return;
        }

        var ltLookup = SystemAPI.GetComponentLookup<LocalTransform>(true);
        var prefabLookup = SystemAPI.GetComponentLookup<ProjectilePrefabRef>(true);

        for (int i = 0; i < rpcEntities.Length; i++)
        {
            var rpcEntity = rpcEntities[i];
            var req = em.GetComponentData<ReceiveRpcCommandRequest>(rpcEntity);
            var rpc = em.GetComponentData<FireProjectileRpc>(rpcEntity);

            var conn = req.SourceConnection;

            // shooter 찾기 (CommandTarget 우선, 없으면 NetworkId -> GhostOwner)
            Entity shooter = Entity.Null;

            if (em.HasComponent<CommandTarget>(conn))
            {
                var ct = em.GetComponentData<CommandTarget>(conn);
                if (ct.targetEntity != Entity.Null && em.Exists(ct.targetEntity))
                    shooter = ct.targetEntity;
            }

            if (shooter == Entity.Null && em.HasComponent<NetworkId>(conn))
            {
                int nid = em.GetComponentData<NetworkId>(conn).Value;

                foreach (var (owner, ent) in SystemAPI.Query<RefRO<GhostOwner>>().WithEntityAccess())
                {
                    if (owner.ValueRO.NetworkId != nid) continue;
                    if (!ltLookup.HasComponent(ent)) continue;

                    // ProjectilePrefabRef 가진 엔티티(플레이어) 우선
                    if (prefabLookup.HasComponent(ent))
                    {
                        shooter = ent;
                        break;
                    }

                    if (shooter == Entity.Null)
                        shooter = ent;
                }
            }

            // shooter 못 찾으면 RPC만 소비
            if (shooter == Entity.Null || !ltLookup.HasComponent(shooter))
            {
                em.DestroyEntity(rpcEntity);
                continue;
            }

            var shooterTf = ltLookup[shooter];

            // ✅ 마우스 월드좌표로 방향 계산
            float3 dir = math.normalizesafe(rpc.TargetPosition - shooterTf.Position, math.forward(shooterTf.Rotation));

            // 스폰 위치(가까이 쏘는 건 그대로 유지)
            float3 spawnPos = shooterTf.Position + dir * 0.6f;

            // 회전도 dir 바라보게
            quaternion rot = quaternion.LookRotationSafe(dir, math.up());

            var proj = em.Instantiate(projectilePrefab);

            // 위치/회전 세팅
            var projTf = LocalTransform.FromPositionRotationScale(spawnPos, rot, 1f);
            if (em.HasComponent<LocalTransform>(proj)) em.SetComponentData(proj, projTf);
            else em.AddComponentData(proj, projTf);

            // ✅ 이동값 세팅 (안 움직이는 문제 + 즉시 despawn 방지)
            if (em.HasComponent<ProjectileMove>(proj))
            {
                var mv = em.GetComponentData<ProjectileMove>(proj);

                mv.Direction = dir;               // 마우스 방향으로 이동
                if (mv.Speed <= 0f) mv.Speed = 20f; // 프리팹 Speed가 0이면 최소 보정
                if (mv.RemainingDistance <= 0f) mv.RemainingDistance = 30f; // 0이면 DespawnSystem이 즉시 삭제

                em.SetComponentData(proj, mv);
            }

            // RPC 소비
            em.DestroyEntity(rpcEntity);
        }
    }
}
