using Unity.Entities;
using Unity.Burst;
using Unity.Transforms;
using Unity.Mathematics;
using Unity.NetCode;
using Shared;

namespace Shared
{
    /// <summary>
    /// CarriedResource 엔티티가 Worker를 따라다니도록 위치 업데이트
    /// - 위치: Worker 머리 위 (Y + 1.2f)
    /// - 가시성: WorkerState.CarriedAmount > 0 일 때만 Scale 1, 아니면 Scale 0
    /// Ghost 동기화 이후 + Transform 계산 이전에 실행
    /// </summary>
    [UpdateInGroup(typeof(TransformSystemGroup))]
    [BurstCompile]
    public partial struct CarriedResourceFollowSystem : ISystem
    {
        private ComponentLookup<LocalTransform> _transformLookup;
        private ComponentLookup<WorkerState> _workerStateLookup;

        public void OnCreate(ref SystemState state)
        {
            _transformLookup = state.GetComponentLookup<LocalTransform>(true);
            _workerStateLookup = state.GetComponentLookup<WorkerState>(true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            _transformLookup.Update(ref state);
            _workerStateLookup.Update(ref state);

            foreach (var (owner, transform)
                in SystemAPI.Query<RefRO<CarriedResourceOwner>, RefRW<LocalTransform>>()
                    .WithAll<CarriedResourceTag>())
            {
                Entity workerEntity = owner.ValueRO.WorkerEntity;

                if (workerEntity == Entity.Null) continue;
                if (!_transformLookup.HasComponent(workerEntity)) continue;
                if (!_workerStateLookup.HasComponent(workerEntity)) continue;

                // Worker 상태 확인
                var workerState = _workerStateLookup[workerEntity];
                bool hasResource = workerState.CarriedAmount > 0;

                // Worker의 위치 가져오기
                float3 workerPos = _transformLookup[workerEntity].Position;

                // 위치 업데이트 (머리 위 오프셋)
                transform.ValueRW.Position = workerPos + new float3(0, 1.2f, 0);

                // Scale로 가시성 제어 (0 = 숨김, 1 = 표시)
                transform.ValueRW.Scale = hasResource ? 1f : 0f;
            }
        }
    }
}
