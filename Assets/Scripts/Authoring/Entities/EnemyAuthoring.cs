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
                AddComponent(entity, new Target
                {
                    TargetEntity = Entity.Null,
                    HasTarget = false,
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
                }

                // =======================================================================
                // 5. [렌더링] - 적 시각적 구분
                // =======================================================================
                AddComponent(entity, new URPMaterialPropertyBaseColor
                {
                    Value = new float4(0, 0, 0, 1)  // 흑색
                });
            }
        }
    }
}
