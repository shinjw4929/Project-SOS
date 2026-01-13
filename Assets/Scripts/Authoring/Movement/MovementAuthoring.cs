using Unity.Entities;
using Unity.Mathematics;
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

        [Header("Y Position Lock")]
        [Tooltip("Y축 위치를 초기값으로 고정 (비행 유닛은 비활성화)")]
        public bool LockYPosition = true;

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
                // 6. Y Position Lock (물리 충돌로 인한 떠오름 방지)
                // ==========================================================
                if (authoring.LockYPosition)
                {
                    AddComponent(entity, new LockedYPosition
                    {
                        Value = authoring.transform.position.y
                    });
                }
            }
        }
    }
}
