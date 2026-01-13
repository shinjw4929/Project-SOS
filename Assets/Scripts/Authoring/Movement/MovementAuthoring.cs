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
        [Header("Stats")]
        [Tooltip("이동 속도")]
        public float MoveSpeed = 5.0f;

        [Header("Pathfinding")]
        [Tooltip("도착 판정 반경")]
        public float ArrivalRadius = 0.5f;

        [Header("NavMesh Settings")]
        [Tooltip("Unity Navigation Agents 탭에서의 순서 (0=첫번째, 1=두번째, ...)")]
        public int AgentTypeIndex = 0;

        public class Baker : Baker<MovementAuthoring>
        {
            public override void Bake(MovementAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);

                // ==========================================================
                // 1. Stats (이동 속도)
                // ==========================================================
                AddComponent(entity, new MovementSpeed { Value = authoring.MoveSpeed });

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
            }
        }
    }
}
