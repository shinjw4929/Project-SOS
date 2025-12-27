using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;
using Shared;

namespace Authoring
{
    public class UnitAuthoring : MonoBehaviour
    {
        // 인스펙터용 Enum (ECS 로직에는 안 들어가고, 베이킹 분기용으로만 사용)
        public enum AuthoringUnitType
        {
            Hero,
            Worker,
            Soldier
        }

        [Header("Identity")]
        public AuthoringUnitType unitType = AuthoringUnitType.Soldier;
        
        [Header("Production Info (Prefab Data)")]
        public int cost = 100;
        public int populationCost = 1;
        public float trainingTime = 5.0f;
 
        [Header("Base Status")]
        public float moveSpeed = 25.0f;
        public float reach = 1.0f;
        public float maxHealth = 100.0f;
        public float defense = 0.0f;
        public float visionRange = 10.0f;

        [Header("Combat Status")]
        public float attackPower = 1.0f;
        public float attackSpeed = 1.0f;

        public class Baker : Baker<UnitAuthoring>
        {
            public override void Bake(UnitAuthoring authoring)
            {
                // 유닛은 움직여야 하므로 Dynamic
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);
                
                // =======================================================================
                // 1. [정체성] 태그 부착 (UnitType Enum 대신 태그 사용)
                // =======================================================================
                AddComponent(entity, new UnitTag()); // "나는 유닛이다"

                switch (authoring.unitType)
                {
                    case AuthoringUnitType.Hero:
                        AddComponent(entity, new HeroTag());
                        AddComponent(entity, new WorkerTag());
                        AddComponent(entity, new BuilderTag());
                        break;
                    case AuthoringUnitType.Worker:
                        AddComponent(entity, new WorkerTag());
                        AddComponent(entity, new BuilderTag());
                        break;
                    case AuthoringUnitType.Soldier:
                        AddComponent(entity, new SoldierTag());
                        break;
                }

                // =======================================================================
                // 2. [생산 정보] 프리팹 데이터 (UnitMetadata 대체)
                // =======================================================================
                // 생산 비용 (자원 + 인구수)
                AddComponent(entity, new ProductionCost
                {
                    Cost= authoring.cost,
                    PopulationCost = authoring.populationCost
                });

                // 생산 시간
                AddComponent(entity, new ProductionInfo
                {
                    ProductionTime = authoring.trainingTime
                });

                // =======================================================================
                // 3. [기본 스탯] 초기값 주입 (EntityStatus/Metadata 통합)
                // =======================================================================
                
                // 체력
                AddComponent(entity, new Health
                {
                    CurrentValue = authoring.maxHealth,
                    MaxValue = authoring.maxHealth
                });
                
                // 이동 속도 (이동 불가능한 유닛이면 0으로 하거나 아예 안 붙임)
                AddComponent(entity, new MovementSpeed
                {
                    Value = authoring.moveSpeed
                });
                
                // 사거리
                AddComponent(entity, new Reach
                {
                    Value = authoring.reach,
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
                
                
                
                
                // =======================================================================
                // 4. [전투] 공격 가능 여부 체크
                // =======================================================================
                if (authoring.attackPower > 0)
                {
                    AddComponent(entity, new CombatStatus
                    {
                        AttackPower = authoring.attackPower,
                        AttackSpeed = authoring.attackSpeed
                    });
                }

                // =======================================================================
                // 5. [경제] 일꾼 전용 데이터
                // =======================================================================
                // 태그가 WorkerTag인 경우 자원 주머니 부착
                if (authoring.unitType == AuthoringUnitType.Worker || authoring.unitType == AuthoringUnitType.Hero)
                {
                    AddComponent(entity, new WorkerState
                    {
                        CarriedAmount = 0,
                        CarriedType = ResourceType.None,
                        GatheringProgress = 0f
                    });
                }
                
                // =======================================================================
                // 6. [공통] 필수 상태 및 렌더링
                // =======================================================================
                // 상태 머신용 Enum (동기화용)
                AddComponent(entity, new UnitState
                {
                    CurrentState = UnitContext.Idle,
                    //StateStartTime = 0 // 시스템에서 Time.ElapsedTime으로 갱신
                });
                
                AddComponent(entity, new Target
                {
                    TargetEntity = Entity.Null,
                });
                
                // 팀 ID (기본값 0)
                AddComponent(entity, new Team { teamId = 0 });

                // 컬러링용 URP 프로퍼티
                AddComponent(entity, new URPMaterialPropertyBaseColor 
                { 
                    Value = new float4(1, 1, 1, 1) 
                });
            }
        }
    }
}