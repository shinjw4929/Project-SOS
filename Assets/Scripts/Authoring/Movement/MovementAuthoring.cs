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

        [Header("NavMesh Settings")]
        [Tooltip("Unity Navigation Agents 탭에서의 순서 (0=첫번째, 1=두번째, ...)")]
        public int AgentTypeIndex = 0;

        [Header("Physics Radius")]
        [Tooltip("물리 충돌 반경 (0이면 Collider에서 자동 추출)")]
        public float PhysicsRadiusOverride = 0f;

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
                    CurrentWaypointIndex = 0
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
                // 4. Buffers (경로 저장소)
                // ==========================================================
                // 경로 탐색 결과 버퍼 (Server Only)
                AddBuffer<PathWaypoint>(entity);

                // ==========================================================
                // 5. NavMesh
                // ==========================================================
                // NavMesh Agent 설정 (유닛 크기별 경로 계산용)
                AddComponent(entity, new NavMeshAgentConfig
                {
                    AgentTypeIndex = authoring.AgentTypeIndex
                });

                // ==========================================================
                // 6. PhysicsRadius (건물 충돌 및 공격 판정용)
                // ==========================================================
                float physicsRadius = authoring.PhysicsRadiusOverride;
                if (physicsRadius <= 0f)
                {
                    // Collider에서 반지름 자동 추출
                    physicsRadius = ExtractColliderRadius(authoring.gameObject);
                }
                AddComponent(entity, new PhysicsRadius
                {
                    Value = physicsRadius
                });

                // ==========================================================
                // 7. Kinematic Mass (LocalTransform 직접 제어)
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

            /// <summary>
            /// GameObject의 Collider에서 대략적인 반지름 추출
            /// </summary>
            private float ExtractColliderRadius(GameObject go)
            {
                // SphereCollider (UnityEngine)
                var sphere = go.GetComponent<UnityEngine.SphereCollider>();
                if (sphere != null)
                {
                    return sphere.radius * Mathf.Max(go.transform.lossyScale.x,
                                                      go.transform.lossyScale.y,
                                                      go.transform.lossyScale.z);
                }

                // CapsuleCollider (UnityEngine)
                var capsule = go.GetComponent<UnityEngine.CapsuleCollider>();
                if (capsule != null)
                {
                    return capsule.radius * Mathf.Max(go.transform.lossyScale.x,
                                                       go.transform.lossyScale.z);
                }

                // BoxCollider (UnityEngine) - 가장 큰 축의 절반
                var box = go.GetComponent<UnityEngine.BoxCollider>();
                if (box != null)
                {
                    Vector3 scaledSize = Vector3.Scale(box.size, go.transform.lossyScale);
                    return Mathf.Max(scaledSize.x, scaledSize.z) * 0.5f;
                }

                // 기본값 (Collider 없음)
                return 0.5f;
            }
        }
    }
}
