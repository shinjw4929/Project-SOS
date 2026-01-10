using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using Unity.NetCode;

namespace Shared
{
    [UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
    [UpdateBefore(typeof(PredictedMovementSystem))]
    [BurstCompile]
    public partial struct UnitSeparationSystem : ISystem
    {
        private ComponentLookup<WorkerState> _workerStateLookup;
        private ComponentLookup<UnitIntentState> _unitIntentStateLookup;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PhysicsWorldSingleton>();
            _workerStateLookup = state.GetComponentLookup<WorkerState>(true);
            _unitIntentStateLookup = state.GetComponentLookup<UnitIntentState>(true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            _workerStateLookup.Update(ref state);
            _unitIntentStateLookup.Update(ref state);

            var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();

            var job = new CalculateSeparationJob
            {
                CollisionWorld = physicsWorld.CollisionWorld,
                // [필터 설정] 유닛끼리만 밀어내도록 설정
                UnitFilter = new CollisionFilter
                {
                    BelongsTo = 1u << 11, // Unit
                    CollidesWith = 1u << 11, // Unit
                    GroupIndex = 0
                },
                WorkerStateLookup = _workerStateLookup,
                UnitIntentStateLookup = _unitIntentStateLookup
            };

            state.Dependency = job.ScheduleParallel(state.Dependency);
        }
    }

    [BurstCompile]
    public partial struct CalculateSeparationJob : IJobEntity
    {
        [ReadOnly] public CollisionWorld CollisionWorld;
        [ReadOnly] public ComponentLookup<WorkerState> WorkerStateLookup;
        [ReadOnly] public ComponentLookup<UnitIntentState> UnitIntentStateLookup;
        public CollisionFilter UnitFilter;

        // SeparationRadius: 유닛끼리 겹치지 않으려는 최소 거리
        private const float SeparationRadius = 0.8f;

        // IEnableableComponent: 움직이는 놈들만 밀어내기 계산 (최적화)
        // 만약 서 있는 유닛도 밀려야 한다면 EnabledRefRO 제거하고 ref SeparationForce만 쓰면 됨
        private void Execute(
            Entity entity,
            in LocalTransform transform,
            ref SeparationForce separationForce,
            EnabledRefRO<MovementWaypoints> isMoving)
        {
            float3 currentPos = transform.Position;
            float3 force = float3.zero;

            // 주변 유닛 검색
            var pointDistanceInput = new PointDistanceInput
            {
                Position = currentPos,
                MaxDistance = SeparationRadius,
                Filter = UnitFilter
            };

            // 스택 메모리 할당 (빠름)
            var hits = new NativeList<DistanceHit>(8, Allocator.Temp);

            if (CollisionWorld.CalculateDistance(pointDistanceInput, ref hits))
            {
                for (int i = 0; i < hits.Length; i++)
                {
                    var hit = hits[i];
                    if (hit.Entity == entity) continue; // 나 자신 제외

                    // 채집 중인 유닛은 밀어내지 않음
                    if (IsGathering(hit.Entity)) continue;

                    float dist = hit.Distance;

                    // 너무 딱 붙어있으면(0) 강제로 떼어냄
                    if (dist < 0.01f)
                    {
                        // 임의의 방향으로 밀기 (인덱스 기반 해시)
                        float angle = (entity.Index % 360) * math.PI / 180f;
                        force += new float3(math.cos(angle), 0f, math.sin(angle));
                        continue;
                    }

                    // 반대 방향 벡터 계산
                    float3 awayDir = currentPos - hit.Position;
                    awayDir.y = 0f;

                    float awayLength = math.length(awayDir);
                    if (awayLength < 0.001f) continue;

                    // 가까울수록 더 세게 밈 (Linear)
                    float strength = 1f - (dist / SeparationRadius);
                    force += (awayDir / awayLength) * strength;
                }
            }

            hits.Dispose(); // NativeList 해제 필수
            separationForce.Force = force; // 계산된 힘 저장
        }

        /// <summary>
        /// 해당 엔티티가 채집 중인지 확인 (Gathering, WaitingForNode, Unloading)
        /// </summary>
        private bool IsGathering(Entity entity)
        {
            // Intent가 Gather가 아니면 채집 중 아님
            if (!UnitIntentStateLookup.TryGetComponent(entity, out UnitIntentState intentState))
                return false;
            if (intentState.State != Intent.Gather)
                return false;

            // Phase가 채집 관련 상태인지 확인
            if (!WorkerStateLookup.TryGetComponent(entity, out WorkerState workerState))
                return false;

            return workerState.Phase == GatherPhase.Gathering ||
                   workerState.Phase == GatherPhase.WaitingForNode ||
                   workerState.Phase == GatherPhase.Unloading;
        }
    }
}