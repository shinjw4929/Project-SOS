using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Shared;

namespace Authoring
{
    /// <summary>
    /// RTS 유닛의 이동 및 명령 시스템을 위한 통합 Authoring
    /// - 프리팹에 이 스크립트 하나만 붙이면 이동 관련 모든 컴포넌트가 베이킹됨
    /// </summary>
    public class UnitMovementAuthoring : MonoBehaviour
    {
        [Header("Stats")]
        [Tooltip("유닛의 이동 속도")]
        public float MoveSpeed = 5.0f;

        [Tooltip("유닛의 회전 속도 (Radians/Sec)")]
        public float TurnSpeed = 10.0f;
        
        [Header("Pathfinding")]
        [Tooltip("도착 판정 반경")]
        public float ArrivalRadius = 0.5f;

        public class Baker : Baker<UnitMovementAuthoring>
        {
            public override void Bake(UnitMovementAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);

                // ==========================================================
                // 1. Stats (설정값)
                // ==========================================================
                AddComponent(entity, new MovementSpeed { Value = authoring.MoveSpeed });
                // (회전 속도 컴포넌트가 있다면 여기에 추가, 없으면 LocalTransform 로직에서 사용)

                // ==========================================================
                // 2. High Level Logic (서버 판단용)
                // ==========================================================
                // 최종 목적지 관리
                AddComponent(entity, new MovementGoal
                {
                    Destination = default,
                    IsPathDirty = false,
                    CurrentWaypointIndex = 0
                });

                // 유닛의 의도 (Move, Attack, Build...)
                AddComponent(entity, new UnitIntentState
                {
                    State = Intent.Idle,
                    TargetEntity = Entity.Null
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
                SetComponentEnabled<MovementWaypoints>(entity, false); // 움직이기 전엔 꺼둠!

                // 유닛 상태 (애니메이션용)
                AddComponent(entity, new UnitActionState
                {
                    State = Action.Idle
                });

                // 유닛끼리 밀어내는 힘
                AddComponent(entity, new SeparationForce
                {
                    Force = float3.zero
                });

                // ==========================================================
                // 4. Buffers (데이터 저장소)
                // ==========================================================
                // 입력 명령 버퍼 (ICommandData)
                // Baker에서 AddBuffer를 호출하면 GhostAuthoring이 감지하여 자동으로 Input Buffer로 등록함
                AddBuffer<UnitCommand>(entity);

                // 경로 탐색 결과 버퍼 (Server Only)
                AddBuffer<PathWaypoint>(entity);
            }
        }
    }
}