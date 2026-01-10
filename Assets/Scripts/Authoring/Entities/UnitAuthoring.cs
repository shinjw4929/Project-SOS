using System.Collections.Generic;
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
            Soldier,    // 일반 병사 (폴백)
            Swordsman,  // 근접 병사
            Trooper,    // 중거리 병사
            Sniper      // 장거리 병사
        }

        [Header("Identity")]
        public AuthoringUnitType unitType = AuthoringUnitType.Soldier;
        
        [Header("Production Info (Prefab Data)")]
        public int cost = 100;
        public int populationCost = 1;
        public float trainingTime = 5.0f;
 
        [Header("Base Status")]
        public float maxHealth = 100.0f;
        public float defense = 0.0f;
        public float visionRange = 10.0f;
        public float workRange = 1.0f;
        
        [Header("Unit Size")]
        public float radius = 1.0f;
        
        [Header("Combat Status")]
        public float attackPower = 0.0f;
        public float attackSpeed = 1.0f;
        public float attackRange = 2.0f;
        
        [Header("Gathering Settings (Worker Only)")]
        [Min(1)] public int maxCarryAmount = 10;
        [Min(1)] public float gatheringSpeed = 1.0f;

        [Header("Builder Settings")]
        public List<GameObject> buildableStructures; // 이 유닛이 건설할 수 있는 건물 프리팹 목록
        
        
        public class Baker : Baker<UnitAuthoring>
        {
            public override void Bake(UnitAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);
                
                // =======================================================================
                // 1. [정체성] 태그 및 역할 플래그 설정
                // =======================================================================
                AddComponent(entity, new UnitTag());
                AddComponent(entity, new Team { teamId = 0 });
                
                // 역할 플래그 (베이킹 로직 분기용)
                bool isWorker   = false;
                bool isBuilder  = false;
                //bool isSoldier  = false;
                
                switch (authoring.unitType)
                {
                    case AuthoringUnitType.Hero:
                        AddComponent(entity, new HeroTag());
                        AddComponent(entity, new WorkerTag());
                        AddComponent(entity, new BuilderTag());
                        isWorker = true;
                        isBuilder =  true;
                        break;
                    case AuthoringUnitType.Worker:
                        AddComponent(entity, new WorkerTag());
                        AddComponent(entity, new BuilderTag());
                        isWorker = true;
                        isBuilder = true;
                        break;
                    case AuthoringUnitType.Soldier:
                        AddComponent(entity, new SoldierTag());
                        break;
                    case AuthoringUnitType.Swordsman:
                        AddComponent(entity, new SoldierTag());
                        AddComponent(entity, new SwordsmanTag());
                        break;
                    case AuthoringUnitType.Trooper:
                        AddComponent(entity, new SoldierTag());
                        AddComponent(entity, new TrooperTag());
                        AddComponent(entity, new RangedUnitTag());
                        break;
                    case AuthoringUnitType.Sniper:
                        AddComponent(entity, new SoldierTag());
                        AddComponent(entity, new SniperTag());
                        AddComponent(entity, new RangedUnitTag());
                        break;
                }
                
                // =======================================================================
                // 2. [Builder 유닛] 버퍼 및 사거리 설정
                // =======================================================================
                if (isBuilder)
                {
                    // 건설 가능 건물 버퍼 생성
                    if (authoring.buildableStructures != null && authoring.buildableStructures.Count > 0)
                    {
                        var buffer = AddBuffer<AvailableStructure>(entity);

                        foreach (var structureObj in authoring.buildableStructures)
                        {
                            if (structureObj == null) continue;

                            // GameObject -> Entity 변환 후 버퍼에 추가
                            buffer.Add(new AvailableStructure
                            {
                                PrefabEntity = GetEntity(structureObj, TransformUsageFlags.Dynamic)
                            });
                        }
                    }
                }
                
                // =======================================================================
                // 3. [Worker 유닛]
                // =======================================================================
                if (isWorker)
                {
                    AddComponent(entity, new GatheringAbility
                    {
                        MaxCarryAmount = authoring.maxCarryAmount,
                        GatheringSpeed = authoring.gatheringSpeed
                    });
                    
                    AddComponent(entity, new WorkerState
                    {
                        CarriedAmount = 0,
                        CarriedType = ResourceType.None,
                        GatheringProgress = 0f,
                        IsInsideNode = false,
                        Phase = GatherPhase.None
                    });

                    AddComponent(entity, new GatheringTarget
                    {
                        ResourceNodeEntity = Entity.Null,
                        ReturnPointEntity = Entity.Null,
                        AutoReturn = true
                    });
                }
                
                // =======================================================================
                // 4. [기본 스탯]
                // =======================================================================
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
                
                // 체력
                AddComponent(entity, new Health
                {
                    CurrentValue = authoring.maxHealth,
                    MaxValue = authoring.maxHealth
                });

                // 데미지 이벤트 버퍼 (지연 데미지 적용용)
                AddBuffer<DamageEvent>(entity);
                
                // 사거리
                AddComponent(entity, new WorkRange
                {
                    Value = authoring.workRange + authoring.radius,
                });
                
                // 채집, 추격, 공격 대상
                AddComponent(entity, new AggroTarget
                {
                    TargetEntity = Entity.Null,
                    LastTargetPosition = float3.zero,
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
                
                // 유닛 반지름 (상호작용/도착 판정용)
                AddComponent(entity, new ObstacleRadius
                {
                    Radius = authoring.radius
                });
                
                // =======================================================================
                // 6. [전투 능력]
                // =======================================================================
                if (authoring.attackPower > 0)
                {
                    AddComponent(entity, new CombatStats
                    {
                        AttackPower = authoring.attackPower,
                        AttackSpeed = authoring.attackSpeed,
                        AttackRange = authoring.attackRange + authoring.radius
                    });

                    // 공격 쿨다운 (초기값 0 = 즉시 공격 가능)
                    AddComponent(entity, new AttackCooldown
                    {
                        RemainingTime = 0f
                    });
                }
                
                // =======================================================================
                // 7. [렌더링]
                // =======================================================================
                // UnitState 제거됨 - UnitIntentState + UnitActionState로 대체 (UnitMovementAuthoring에서 추가)

                AddComponent(entity, new URPMaterialPropertyBaseColor
                {
                    Value = new float4(1, 1, 1, 1)
                });
            }
        }
    }
}