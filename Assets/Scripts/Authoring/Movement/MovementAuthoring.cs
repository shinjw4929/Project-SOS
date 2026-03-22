using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using UnityEngine;
using Shared;

namespace Authoring
{
    /// <summary>
    /// 이동 가능한 엔티티(유닛, 적)를 위한 공용 이동 Authoring
    /// - 모든 이동 관련 컴포넌트를 베이킹
    /// - UnitMovementAuthoring, EnemyAuthoring 등과 함께 사용
    /// </summary>
    public class MovementAuthoring : MonoBehaviour
    {
        [Header("Movement Dynamics")]
        [Tooltip("최대 이동 속도 (m/s)")]
        public float MaxSpeed = 10.0f;

        [Tooltip("가속도 (m/s^2)")]
        public float Acceleration = 180.0f;

        [Tooltip("감속도 (m/s^2)")]
        public float Deceleration = 240.0f;

        [Tooltip("회전 속도 (rad/s)")]
        public float RotationSpeed = 12.0f;

        [Header("Pathfinding")]
        [Tooltip("도착 판정 반경")]
        public float ArrivalRadius = 0.5f;

        [Header("Pathfinding Size")]
        [Tooltip("Small(0): 좁은 통로 통과 가능, Large(1): 벽 사이 갭 차단")]
        public byte PathfindingSize = 0;

        public class Baker : Baker<MovementAuthoring>
        {
            public override void Bake(MovementAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);

                // ==========================================================
                // 1. Movement Dynamics (가속도/감속도 기반 이동)
                // ==========================================================
                AddComponent(entity, new MovementDynamics
                {
                    MaxSpeed = authoring.MaxSpeed,
                    Acceleration = authoring.Acceleration,
                    Deceleration = authoring.Deceleration,
                    RotationSpeed = authoring.RotationSpeed
                });

                // ==========================================================
                // 2. High Level Logic (경로 탐색용)
                // ==========================================================
                // 최종 목적지 관리
                AddComponent(entity, new MovementGoal
                {
                    Destination = default,
                    IsPathDirty = false,
                });

                // ==========================================================
                // 3. Low Level Physics (물리 이동용)
                // ==========================================================
                // 실제 이동 웨이포인트 (초기엔 비활성화)
                AddComponent(entity, new MovementWaypoints
                {
                    Current = float3.zero,
                    Next = float3.zero,
                    HasNext = false,
                    ArrivalRadius = authoring.ArrivalRadius
                });
                SetComponentEnabled<MovementWaypoints>(entity, false);

                // ==========================================================
                // 4. Flow Field 경로탐색
                // ==========================================================
                AddComponent(entity, new GridPathfindingSize
                {
                    CellPadding = authoring.PathfindingSize
                });
                AddComponent(entity, new FlowFieldRef { Key = -1 });

                // ==========================================================
                // 6. Kinematic Mass (LocalTransform 직접 제어)
                // ==========================================================
                // 주의: Unity DOTS Physics는 Rigidbody 컴포넌트가 있으면 자동으로
                // PhysicsMass를 베이킹합니다. 프리팹에서 Rigidbody.isKinematic=true로
                // 설정하는 것이 권장됩니다.
                // Rigidbody가 없는 경우에만 수동으로 Kinematic Mass를 추가합니다.
                var rigidbody = authoring.GetComponent<Rigidbody>();
                if (rigidbody == null)
                {
                    AddComponent(entity, PhysicsMass.CreateKinematic(MassProperties.UnitSphere));
                }
            }
        }
    }
}
