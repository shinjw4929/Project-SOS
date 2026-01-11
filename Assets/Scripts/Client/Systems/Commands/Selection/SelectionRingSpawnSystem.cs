using Unity.Entities;
using Unity.NetCode;
using Unity.Burst;
using Unity.Collections;
using Unity.Transforms;
using Unity.Mathematics;
using Shared;

namespace Client
{
    /// <summary>
    /// 선택 가능한 엔티티에 Selection Ring 자식 엔티티를 자동 생성
    /// - Selected 컴포넌트가 있는 엔티티에 1회 생성
    /// - Scale 0으로 시작 (SelectionVisualizationSystem에서 선택 상태에 따라 토글)
    /// - LinkedEntityGroup으로 부모 삭제 시 자동 정리
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [BurstCompile]
    public partial struct SelectionRingSpawnSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<Shared.SelectionRingPrefabRef>();
            state.RequireForUpdate<NetworkStreamInGame>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingleton(out Shared.SelectionRingPrefabRef prefabRef)) return;
            if (prefabRef.RingPrefab == Entity.Null) return;

            var ecb = new EntityCommandBuffer(Allocator.Temp);

            // Selected + ObstacleRadius가 있고 Ring이 없는 엔티티에 Ring 생성
            foreach (var (radius, entity) in SystemAPI.Query<RefRO<ObstacleRadius>>()
                .WithAll<Selected>()
                .WithNone<SelectionRingLinked>()
                .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                .WithEntityAccess())
            {
                // Ring 엔티티 생성
                Entity ring = ecb.Instantiate(prefabRef.RingPrefab);

                // 소유자 설정
                ecb.SetComponent(ring, new SelectionRingOwner { OwnerEntity = entity });

                // 초기 Transform 설정 (Scale 0으로 숨김)
                ecb.SetComponent(ring, new LocalTransform
                {
                    Position = float3.zero,
                    Rotation = quaternion.Euler(math.radians(90f), 0f, 0f),
                    Scale = 0f
                });

                // LinkedEntityGroup으로 부모 삭제 시 Ring 자동 삭제
                if (!SystemAPI.HasBuffer<LinkedEntityGroup>(entity))
                {
                    ecb.AddBuffer<LinkedEntityGroup>(entity);
                    ecb.AppendToBuffer(entity, new LinkedEntityGroup { Value = entity });
                }
                ecb.AppendToBuffer(entity, new LinkedEntityGroup { Value = ring });

                // 연결 완료 태그 (중복 생성 방지)
                ecb.AddComponent<SelectionRingLinked>(entity);
            }

            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }
    }
}
