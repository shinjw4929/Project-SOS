using Unity.Burst;
using Unity.Entities;
using Unity.NetCode;
using Unity.Mathematics;
using Unity.Transforms;
using Shared;

namespace Client
{
    /// <summary>
    /// Selection Ring 시각화 시스템
    /// - Ring 위치를 Owner 위치로 업데이트
    /// - 선택 상태에 따라 Scale 토글 (0 = 숨김, N = 표시)
    /// - 색상은 SpawnSystem에서 팀별 프리팹으로 결정됨
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(TransformSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial struct SelectionVisualizationSystem : ISystem
    {
        private ComponentLookup<LocalTransform> _transformLookup;
        private ComponentLookup<Selected> _selectedLookup;
        private ComponentLookup<ObstacleRadius> _radiusLookup;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkStreamInGame>();

            _transformLookup = state.GetComponentLookup<LocalTransform>(true);
            _selectedLookup = state.GetComponentLookup<Selected>(true);
            _radiusLookup = state.GetComponentLookup<ObstacleRadius>(true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            _transformLookup.Update(ref state);
            _selectedLookup.Update(ref state);
            _radiusLookup.Update(ref state);

            // Selection Ring 업데이트
            foreach (var (owner, transform) in SystemAPI.Query<
                RefRO<SelectionRingOwner>,
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
                    // Ring 크기 설정 (반지름 기반, 최소 크기 보장)
                    float ringScale = 1f;
                    if (_radiusLookup.HasComponent(ownerEntity))
                    {
                        ringScale = _radiusLookup[ownerEntity].Radius * 2.2f;
                    }
                    ringScale = math.max(ringScale, 1.5f); // 최소 스케일로 테두리 두께 보장
                    transform.ValueRW.Scale = ringScale;
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
    }
}
