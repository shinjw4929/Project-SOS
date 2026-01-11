using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;
using Shared;

namespace Authoring
{
    public class EnemyAuthoring : MonoBehaviour
    {
        [Header("Base Status")]
        public float moveSpeed = 10.0f;
        public float maxHealth = 100.0f;
        public float defense = 0.0f;
        public float visionRange = 10.0f;

        [Header("Collision")]
        [Min(0.1f)] public float radius = 1.5f;
        
        [Header("Combat Status")]
        public float attackPower = 0.0f;
        public float attackSpeed = 1.0f;
        public float attackRange = 2.0f;
        
        [Header("Enemy Status")]
        public float aggroRange  = 15.0f;
        
        class Baker : Baker<EnemyAuthoring>
        {
            public override void Bake(EnemyAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);

                // =======================================================================
                // 1. [정체성] 태그 설정
                // =======================================================================
                AddComponent(entity, new EnemyTag());  // Enemy 특화 쿼리용

                // =======================================================================
                // 2. [기본 스탯]
                // =======================================================================
                // 체력
                AddComponent(entity, new Health
                {
                    CurrentValue = authoring.maxHealth,
                    MaxValue = authoring.maxHealth
                });

                // 데미지 이벤트 버퍼 (지연 데미지 적용용)
                AddBuffer<DamageEvent>(entity);

                // 이동 속도
                AddComponent(entity, new MovementSpeed
                {
                    Value = authoring.moveSpeed
                });
                
                // 방어력
                AddComponent(entity, new Defense
                {
                    Value = authoring.defense
                });
                
                // 시야
                AddComponent(entity, new VisionRange
                {
                    Value = authoring.visionRange
                });
                
                // 추격, 공격 대상
                AddComponent(entity, new AggroTarget
                {
                    TargetEntity = Entity.Null,
                    LastTargetPosition = Vector3.zero,
                });
                
                AddComponent(entity, new EnemyChaseDistance
                {
                    LoseTargetDistance = authoring.aggroRange
                });

                // 적 팀 ID (플레이어 teamId=0과 다른 값)
                AddComponent(entity, new Team { teamId = -1 });

                // =======================================================================
                // 3. [상태 관리]
                // =======================================================================
                AddComponent(entity, new EnemyState
                {
                    CurrentState = EnemyContext.Idle
                });

                // =======================================================================
                // 4. [전투 능력] - attackPower > 0일 때만 (UnitAuthoring 패턴)
                // =======================================================================
                if (authoring.attackPower > 0)
                {
                    AddComponent(entity, new CombatStats
                    {
                        AttackPower = authoring.attackPower,
                        AttackSpeed = authoring.attackSpeed,
                        AttackRange = authoring.attackRange
                    });

                    // 공격 쿨다운 (초기값 0 = 즉시 공격 가능)
                    AddComponent(entity, new AttackCooldown
                    {
                        RemainingTime = 0f
                    });
                }

                // =======================================================================
                // 5. [NavMesh 이동 시스템] - 유닛과 동일한 경로 탐색 사용
                // =======================================================================
                // 최종 목적지 관리 (PathfindingSystem이 사용)
                AddComponent(entity, new MovementGoal
                {
                    Destination = default,
                    IsPathDirty = false,
                    CurrentWaypointIndex = 0
                });

                // 실제 이동 웨이포인트 (초기엔 비활성화)
                AddComponent(entity, new MovementWaypoints
                {
                    Current = float3.zero,
                    Next = float3.zero,
                    HasNext = false,
                    ArrivalRadius = 0.5f  // 적 기본 도착 반경
                });
                SetComponentEnabled<MovementWaypoints>(entity, false); // 타겟 찾기 전까지 비활성화

                // 유닛/적 간 밀어내기 힘
                AddComponent(entity, new SeparationForce
                {
                    Force = float3.zero
                });

                // 경로 탐색 결과 버퍼 (PathfindingSystem이 채움)
                AddBuffer<PathWaypoint>(entity);

                // 충돌 반경 (도착 판정용)
                AddComponent(entity, new ObstacleRadius
                {
                    Radius = authoring.radius  // 적 기본 반경
                });

                // =======================================================================
                // 6. [렌더링] - 적 시각적 구분
                // =======================================================================
                AddComponent(entity, new URPMaterialPropertyBaseColor
                {
                    Value = new float4(0, 0, 0, 1)  // 흑색
                });
            }
        }
    }
}
