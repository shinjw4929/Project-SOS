using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using Unity.Jobs;
using Shared;

namespace Server
{
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
    public partial struct FireProjectileServerSystem : ISystem
    {
        private EntityQuery _prefabRefQuery;
        private EntityQuery _ghostOwnerQuery;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();

            _prefabRefQuery = state.GetEntityQuery(ComponentType.ReadOnly<ProjectilePrefabRef>());

            // 최적화를 위해 GhostOwner 데이터만 따로 쿼리
            _ghostOwnerQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<GhostOwner>(),
                ComponentType.ReadOnly<LocalTransform>()
            );
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // 1. Prefab 확인
            if (_prefabRefQuery.IsEmptyIgnoreFilter) return;
            var prefabRef = _prefabRefQuery.GetSingleton<ProjectilePrefabRef>();
            Entity projectilePrefab = prefabRef.Prefab;
            if (projectilePrefab == Entity.Null) return;

            // 2. GhostOwner Map 생성 (Job 1)
            // RPC 처리 시 'CommandTarget'이 없을 경우를 대비해 NetworkId -> Entity 매핑을 미리 만듭니다.
            int ghostCount = _ghostOwnerQuery.CalculateEntityCount();
            var ghostOwnerMap = new NativeParallelHashMap<int, Entity>(ghostCount, Allocator.TempJob);

            var mapJob = new BuildGhostOwnerMapJob
            {
                GhostOwners = _ghostOwnerQuery.ToComponentDataArray<GhostOwner>(Allocator.TempJob),
                GhostEntities = _ghostOwnerQuery.ToEntityArray(Allocator.TempJob),
                Map = ghostOwnerMap.AsParallelWriter()
            };
            state.Dependency = mapJob.Schedule(ghostCount, 64, state.Dependency);

            // 3. RPC 처리 (Job 2 - IJobEntity)
            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            
            var fireJob = new FireProjectileJob
            {
                Ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter(),
                ProjectilePrefab = projectilePrefab,
                GhostOwnerMap = ghostOwnerMap, // ReadOnly로 전달
                
                // 랜덤 액세스가 필요한 컴포넌트들은 Lookup으로 전달
                CommandTargetLookup = SystemAPI.GetComponentLookup<CommandTarget>(true),
                NetworkIdLookup = SystemAPI.GetComponentLookup<NetworkId>(true),
                TransformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true),
                TeamLookup = SystemAPI.GetComponentLookup<Team>(true)
            };

            state.Dependency = fireJob.ScheduleParallel(state.Dependency);
            
            // HashMap은 Job 완료 후 해제
            ghostOwnerMap.Dispose(state.Dependency);
        }
    }

    /// <summary>
    /// Step 1: NetworkId -> Entity 매핑 생성 잡
    /// </summary>
    [BurstCompile]
    public struct BuildGhostOwnerMapJob : IJobParallelFor
    {
        [DeallocateOnJobCompletion] public NativeArray<GhostOwner> GhostOwners;
        [DeallocateOnJobCompletion] public NativeArray<Entity> GhostEntities;
        public NativeParallelHashMap<int, Entity>.ParallelWriter Map;

        public void Execute(int index)
        {
            // 같은 ID가 있어도 덮어쓰거나 무시 (보통 유니크함)
            Map.TryAdd(GhostOwners[index].NetworkId, GhostEntities[index]);
        }
    }

    /// <summary>
    /// Step 2: RPC 처리 및 투사체 발사 (IJobEntity 사용)
    /// </summary>
    [BurstCompile]
    public partial struct FireProjectileJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter Ecb;
        public Entity ProjectilePrefab;

        [ReadOnly] public NativeParallelHashMap<int, Entity> GhostOwnerMap;
        
        // 내가 현재 돌고 있는 엔티티(RPC)가 아닌, '다른' 엔티티(Shooter, Connection)의 정보가 필요하므로 Lookup 사용
        [ReadOnly] public ComponentLookup<CommandTarget> CommandTargetLookup;
        [ReadOnly] public ComponentLookup<NetworkId> NetworkIdLookup;
        [ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;
        [ReadOnly] public ComponentLookup<Team> TeamLookup;

        // IJobEntity는 쿼리 조건에 맞는 엔티티(RPC)를 자동으로 필터링해서 Execute에 넣어줍니다.
        private void Execute(Entity rpcEntity, [EntityIndexInQuery] int sortKey, in FireProjectileRpc rpc, in ReceiveRpcCommandRequest req)
        {
            Entity connection = req.SourceConnection;
            Entity shooter = Entity.Null;

            // 1. Shooter 찾기: CommandTarget (Fast Path)
            if (CommandTargetLookup.HasComponent(connection))
            {
                var targetEnt = CommandTargetLookup[connection].targetEntity;
                if (targetEnt != Entity.Null && TransformLookup.HasComponent(targetEnt))
                {
                    shooter = targetEnt;
                }
            }

            // 2. Shooter 찾기: GhostOwner Map (Fallback)
            if (shooter == Entity.Null && NetworkIdLookup.HasComponent(connection))
            {
                int netId = NetworkIdLookup[connection].Value;
                if (GhostOwnerMap.TryGetValue(netId, out Entity ghostEnt))
                {
                    if (TransformLookup.HasComponent(ghostEnt))
                    {
                        shooter = ghostEnt;
                    }
                }
            }

            // Shooter가 없으면 RPC 삭제하고 종료
            if (shooter == Entity.Null)
            {
                Ecb.DestroyEntity(sortKey, rpcEntity);
                return;
            }

            // 3. 발사 로직
            var shooterTf = TransformLookup[shooter];
            
            float3 dir = math.normalizesafe(rpc.TargetPosition - shooterTf.Position, math.forward(shooterTf.Rotation));
            float3 spawnPos = shooterTf.Position + dir * 0.6f;
            quaternion rot = quaternion.LookRotationSafe(dir, math.up());

            // Instantiate
            Entity proj = Ecb.Instantiate(sortKey, ProjectilePrefab);
            
            // Transform 설정
            Ecb.SetComponent(sortKey, proj, LocalTransform.FromPositionRotationScale(spawnPos, rot, 1f));
            
            // Move 설정
            Ecb.AddComponent(sortKey, proj, new ProjectileMove
            {
                Direction = dir,
                Speed = 20f,
                RemainingDistance = 30f
            });

            // 4. 팀 정보 복사 (아군 오폭 방지 핵심)
            if (TeamLookup.HasComponent(shooter))
            {
                Ecb.AddComponent(sortKey, proj, TeamLookup[shooter]);
            }

            // RPC 요청 엔티티 삭제
            Ecb.DestroyEntity(sortKey, rpcEntity);
        }
    }
}