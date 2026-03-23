using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;
using Shared;

namespace Authoring
{
    /// <summary>
    /// 적 엔티티 Authoring
    /// - 이동 관련 컴포넌트는 MovementAuthoring에서 처리
    /// </summary>
    public class EnemyAuthoring : MonoBehaviour
    {
        [Header("Base Status")]
        public float maxHealth = 100.0f;
        public float defense = 0.0f;
        public float visionRange = 10.0f;

        [Header("Obstacle Radius")]
        [FormerlySerializedAs("radius")]
        [Min(0.1f)] public float obstacleRadius = 1.5f;

        [Header("Combat Status")]
        public float attackPower = 0.0f;
        public float attackSpeed = 1.0f;
        public float attackRange = 2.0f;
        [Tooltip("시각 투사체 속도 (원거리 전용, m/s)")]
        public float projectileSpeed = 30f;

        [Header("Attack Type")]
        public bool isRanged = false;
        public bool isFlying = false;

        class Baker : Baker<EnemyAuthoring>
        {
            public override void Bake(EnemyAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);

                // =======================================================================
                // 1. [정체성] 태그 설정
                // =======================================================================
                AddComponent(entity, new EnemyTag());

                // 원거리 적 태그
                if (authoring.isRanged)
                {
                    AddComponent(entity, new RangedEnemyTag());
                }

                // 공중 유닛 태그
                if (authoring.isFlying)
                {
                    AddComponent(entity, new FlyingTag());
                }

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
                    LastTargetPosition = float3.zero,
                });

                // 어그로 고정 (피격 시 N초간 타겟 고정)
                AddComponent(entity, new AggroLock
                {
                    LockedTarget = Entity.Null,
                    RemainingLockTime = 0f,
                    LockDuration = 3.0f // GameSettings.AggroLockDuration과 동기화 필요
                });

                // 적 팀 ID (-1)
                AddComponent(entity, new Team { teamId = -1 });

                // =======================================================================
                // 3. [상태 관리]
                // =======================================================================
                AddComponent(entity, new EnemyState
                {
                    CurrentState = EnemyContext.Idle
                });

                // =======================================================================
                // 4. [전투 능력] - attackPower > 0일 때만
                // =======================================================================
                if (authoring.attackPower > 0)
                {
                    AddComponent(entity, new CombatStats
                    {
                        AttackPower = authoring.attackPower,
                        AttackSpeed = authoring.attackSpeed,
                        AttackRange = authoring.attackRange,
                        ProjectileSpeed = authoring.projectileSpeed
                    });

                    // 공격 쿨다운 (초기값 0 = 즉시 공격 가능)
                    AddComponent(entity, new AttackCooldown
                    {
                        RemainingTime = 0f
                    });
                }

                // 충돌 반경 (도착 판정용)
                AddComponent(entity, new ObstacleRadius
                {
                    Radius = authoring.obstacleRadius
                });

                // 이동 관련 컴포넌트(MovementDynamics, MovementGoal, MovementWaypoints,
                // GridPathfindingSize, FlowFieldRef)는 MovementAuthoring에서 처리

                // URPMaterialPropertyBaseColor + TeamColorTarget은 런타임 TeamColorSystem에서 자식 메시 엔티티에 자동 부착
            }
        }
    }
}
