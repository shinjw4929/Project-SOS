using Unity.Burst;
using Unity.Entities;
using Unity.NetCode;
using Unity.Rendering;
using Unity.Mathematics;
using Unity.Transforms;
using Shared;

namespace Client
{
    /// <summary>
    /// Selection Ring 시각화 시스템
    /// - Ring 위치를 Owner 위치로 업데이트
    /// - 선택 상태에 따라 Scale 토글 (0 = 숨김, N = 표시)
    /// - Team/EnemyTag에 따라 색상 설정 (초록/빨강/노랑)
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(TransformSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct SelectionVisualizationSystem : ISystem
    {
        private ComponentLookup<LocalTransform> _transformLookup;
        private ComponentLookup<Selected> _selectedLookup;
        private ComponentLookup<Team> _teamLookup;
        private ComponentLookup<EnemyTag> _enemyTagLookup;
        private ComponentLookup<ObstacleRadius> _radiusLookup;

        // 색상 상수 (Emission)
        private static readonly float4 ColorAlly = new float4(0f, 1f, 0f, 1f);     // 초록색 (아군)
        private static readonly float4 ColorEnemy = new float4(1f, 0f, 0f, 1f);    // 빨간색 (적)
        private static readonly float4 ColorNeutral = new float4(1f, 1f, 0f, 1f);  // 노란색 (중립)

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkStreamInGame>();
            state.RequireForUpdate<NetworkId>();

            _transformLookup = state.GetComponentLookup<LocalTransform>(true);
            _selectedLookup = state.GetComponentLookup<Selected>(true);
            _teamLookup = state.GetComponentLookup<Team>(true);
            _enemyTagLookup = state.GetComponentLookup<EnemyTag>(true);
            _radiusLookup = state.GetComponentLookup<ObstacleRadius>(true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            _transformLookup.Update(ref state);
            _selectedLookup.Update(ref state);
            _teamLookup.Update(ref state);
            _enemyTagLookup.Update(ref state);
            _radiusLookup.Update(ref state);

            // 내 팀 ID 획득
            if (!SystemAPI.TryGetSingleton<NetworkId>(out var networkId)) return;
            int myTeamId = networkId.Value;

            // Selection Ring 업데이트
            foreach (var (owner, baseColor, transform) in SystemAPI.Query<
                RefRO<SelectionRingOwner>,
                RefRW<URPMaterialPropertyBaseColor>,
                RefRW<LocalTransform>>()
                .WithAll<SelectionRingTag>())
            {
                Entity ownerEntity = owner.ValueRO.OwnerEntity;

                // Owner 유효성 검사
                if (ownerEntity == Entity.Null) continue;
                if (!_transformLookup.HasComponent(ownerEntity)) continue;

                // Owner의 위치 가져오기
                float3 ownerPos = _transformLookup[ownerEntity].Position;

                // Ring 위치 업데이트 (지면에서 약간 위)
                transform.ValueRW.Position = ownerPos + new float3(0f, 0.05f, 0f);

                // 선택 상태 확인
                bool isSelected = false;
                if (_selectedLookup.HasComponent(ownerEntity))
                {
                    isSelected = state.EntityManager.IsComponentEnabled<Selected>(ownerEntity);
                }

                // Scale로 가시성 제어
                if (isSelected)
                {
                    // Ring 크기 설정 (반지름 기반)
                    float ringScale = 1f;
                    if (_radiusLookup.HasComponent(ownerEntity))
                    {
                        ringScale = _radiusLookup[ownerEntity].Radius * 2.2f;
                    }
                    transform.ValueRW.Scale = ringScale;

                    // 색상 결정
                    float4 targetColor = DetermineRingColor(ownerEntity, myTeamId);
                    if (!math.all(baseColor.ValueRO.Value == targetColor))
                    {
                        baseColor.ValueRW.Value = targetColor;
                    }
                }
                else
                {
                    // 선택 해제 → Scale 0으로 숨김
                    if (transform.ValueRO.Scale != 0f)
                    {
                        transform.ValueRW.Scale = 0f;
                    }
                }
            }
        }

        private float4 DetermineRingColor(Entity ownerEntity, int myTeamId)
        {
            // 1. EnemyTag 확인 (적)
            if (_enemyTagLookup.HasComponent(ownerEntity))
            {
                return ColorEnemy;
            }

            // 2. Team 컴포넌트 확인
            if (_teamLookup.HasComponent(ownerEntity))
            {
                int ownerTeamId = _teamLookup[ownerEntity].teamId;

                // teamId == -1: 적
                if (ownerTeamId == -1)
                {
                    return ColorEnemy;
                }

                // teamId == myTeamId: 아군
                if (ownerTeamId == myTeamId)
                {
                    return ColorAlly;
                }

                // 다른 플레이어: 중립
                return ColorNeutral;
            }

            // 3. Team 없음 (자원 노드 등): 중립
            return ColorNeutral;
        }
    }
}
