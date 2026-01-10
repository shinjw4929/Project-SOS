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
    /// Worker 이동 시스템(PredictedMovementSystem) 이후에 실행
    /// </summary>
    [UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
    [UpdateAfter(typeof(PredictedMovementSystem))]
    [BurstCompile]
    public partial struct CarriedResourceFollowSystem : ISystem
    {
        private ComponentLookup<LocalTransform> _transformLookup;

        public void OnCreate(ref SystemState state)
        {
            _transformLookup = state.GetComponentLookup<LocalTransform>(true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            _transformLookup.Update(ref state);

            foreach (var (owner, transform)
                in SystemAPI.Query<RefRO<CarriedResourceOwner>, RefRW<LocalTransform>>()
                    .WithAll<CarriedResourceTag>())
            {
                Entity workerEntity = owner.ValueRO.WorkerEntity;

                if (workerEntity == Entity.Null) continue;
                if (!_transformLookup.HasComponent(workerEntity)) continue;

                // Worker의 위치 가져오기
                float3 workerPos = _transformLookup[workerEntity].Position;

                // 머리 위 오프셋 적용 (즉시 동기화)
                transform.ValueRW.Position = workerPos + new float3(0, 1.2f, 0);
            }
        }
    }
}
