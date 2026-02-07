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
    /// - 팀에 따라 다른 색상의 Ring 프리팹 사용 (Ally/Enemy/Neutral)
    /// - Scale 0으로 시작 (SelectionVisualizationSystem에서 선택 상태에 따라 토글)
    /// - LinkedEntityGroup으로 부모 삭제 시 자동 정리
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [BurstCompile]
    public partial struct SelectionRingSpawnSystem : ISystem
    {
        private ComponentLookup<Team> _teamLookup;
        private ComponentLookup<EnemyTag> _enemyTagLookup;
        private ComponentLookup<GhostOwnerIsLocal> _ghostOwnerIsLocalLookup;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<Shared.SelectionRingPrefabRef>();
            state.RequireForUpdate<NetworkStreamInGame>();

            _teamLookup = state.GetComponentLookup<Team>(true);
            _enemyTagLookup = state.GetComponentLookup<EnemyTag>(true);
            _ghostOwnerIsLocalLookup = state.GetComponentLookup<GhostOwnerIsLocal>(true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingleton(out Shared.SelectionRingPrefabRef prefabRef)) return;

            // GhostOwnerIsLocal이 UpdateGhostOwnerIsLocal Job에서 쓰기 중이므로 의존성 완료 필요
            state.CompleteDependency();

            _teamLookup.Update(ref state);
            _enemyTagLookup.Update(ref state);
            _ghostOwnerIsLocalLookup.Update(ref state);

            var ecb = new EntityCommandBuffer(Allocator.Temp);

            // Selected + ObstacleRadius가 있고 Ring이 없는 엔티티에 Ring 생성
            foreach (var (radius, entity) in SystemAPI.Query<RefRO<ObstacleRadius>>()
                .WithAll<Selected>()
                .WithNone<SelectionRingLinked>()
                .WithEntityAccess())
            {
                // 팀에 따라 프리팹 선택
                Entity ringPrefab = DetermineRingPrefab(entity, prefabRef);
                if (ringPrefab == Entity.Null) continue;

                // Ring 엔티티 생성
                Entity ring = ecb.Instantiate(ringPrefab);

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

        private Entity DetermineRingPrefab(Entity ownerEntity, SelectionRingPrefabRef prefabRef)
        {
            // 1. EnemyTag → 빨강
            if (_enemyTagLookup.HasComponent(ownerEntity))
                return prefabRef.EnemyRingPrefab;

            // 2. Team.teamId == -1 → 빨강
            if (_teamLookup.HasComponent(ownerEntity) && _teamLookup[ownerEntity].teamId == -1)
                return prefabRef.EnemyRingPrefab;

            // 3. GhostOwnerIsLocal (enabled) → 초록 (Netcode가 관리하는 정확한 소유권 체크)
            if (_ghostOwnerIsLocalLookup.HasComponent(ownerEntity) &&
                _ghostOwnerIsLocalLookup.IsComponentEnabled(ownerEntity))
                return prefabRef.AllyRingPrefab;

            // 4. Team 있으나 내 소유 아님 → 노랑 (다른 플레이어)
            if (_teamLookup.HasComponent(ownerEntity))
                return prefabRef.NeutralRingPrefab;

            // 5. 기본값 → 노랑
            return prefabRef.NeutralRingPrefab;
        }
    }
}
