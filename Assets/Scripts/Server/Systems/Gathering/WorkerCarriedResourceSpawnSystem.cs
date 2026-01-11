using Unity.Entities;
using Unity.NetCode;
using Unity.Burst;
using Unity.Collections;
using Shared;

namespace Server
{
    /// <summary>
    /// Worker 엔티티 생성 시 CarriedResource 엔티티를 자동으로 생성하고 연결
    /// - Worker마다 1개의 CarriedResource를 미리 생성
    /// - Scale 0으로 시작 (CarriedResourceFollowSystem에서 CarriedAmount에 따라 토글)
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [BurstCompile]
    public partial struct WorkerCarriedResourceSpawnSystem : ISystem
    {
        private EntityQuery _workerWithoutResourceQuery;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<CarriedResourcePrefabRef>();

            // WorkerTag가 있지만 아직 CarriedResource가 연결되지 않은 Worker 찾기
            // CarriedResourceOwner에서 역참조하는 대신, Worker에 태그를 추가하여 추적
            _workerWithoutResourceQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<WorkerTag>(),
                ComponentType.ReadOnly<WorkerState>(),
                ComponentType.Exclude<WorkerCarriedResourceLinked>()
            );
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (_workerWithoutResourceQuery.IsEmpty) return;

            var prefabRef = SystemAPI.GetSingleton<CarriedResourcePrefabRef>();
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            foreach (var (workerState, entity) in SystemAPI.Query<RefRO<WorkerState>>()
                .WithAll<WorkerTag>()
                .WithNone<WorkerCarriedResourceLinked>()
                .WithEntityAccess())
            {
                // CarriedResource 엔티티 생성
                Entity carriedResource = ecb.Instantiate(prefabRef.CheesePrefab);

                // Worker에 연결
                ecb.SetComponent(carriedResource, new CarriedResourceOwner { WorkerEntity = entity });

                // Worker 삭제 시 CarriedResource도 함께 삭제되도록 LinkedEntityGroup에 추가
                // LinkedEntityGroup 버퍼가 없으면 먼저 추가
                if (!SystemAPI.HasBuffer<LinkedEntityGroup>(entity))
                {
                    ecb.AddBuffer<LinkedEntityGroup>(entity);
                    // 자기 자신도 LinkedEntityGroup에 포함해야 함 (Unity 규칙)
                    ecb.AppendToBuffer(entity, new LinkedEntityGroup { Value = entity });
                }
                ecb.AppendToBuffer(entity, new LinkedEntityGroup { Value = carriedResource });

                // Worker에 연결 완료 태그 추가 (중복 생성 방지)
                ecb.AddComponent<WorkerCarriedResourceLinked>(entity);
            }

            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }
    }

    /// <summary>
    /// Worker에 CarriedResource가 연결되었음을 표시하는 태그
    /// 중복 생성 방지용
    /// </summary>
    public struct WorkerCarriedResourceLinked : IComponentData { }
}