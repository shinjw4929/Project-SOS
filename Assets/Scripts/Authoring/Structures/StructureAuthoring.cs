using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;
using Shared;

namespace Authoring
{
    public class StructureAuthoring : MonoBehaviour
    {
        // 인스펙터에서 건물 종류를 고르기 위한 "베이킹 전용" Enum
        // (실제 ECS 로직에서는 이 Enum을 쓰지 않고 태그를 씁니다)
        public enum AuthoringStructureType
        {
            Wall,           // 단순 벽
            Barracks,       // 유닛 생산
            Turret,         // 공격 타워
            ResourceCenter  // 자원 반납
        }

        [Header("Identity")]
        public AuthoringStructureType structureType = AuthoringStructureType.Wall;

        [Header("Grid Size")]
        [Min(1)] public int width = 1;
        [Min(1)] public int length = 1;
        public float height = 1;
        
        [Header("Production Info (Prefab Data)")]
        public int cost = 100;
        public float buildTime = 10.0f;

        [Header("Base Stats")]
        public float maxHealth = 500.0f;
        public float defense = 1.0f;
        public float visionRange = 10.0f;

        [Header("Combat Stats (Turret Only)")]
        public float attackDamage = 0.0f;
        public float attackRange = 0.0f;
        public float attackSpeed = 0.0f;

        [Header("Self-Destruct (Wall Only)")]
        public float explosionRadius = 3.0f;
        public float explosionDamage = 100.0f;
        public float explosionDelay = 0.5f;

        [Header("Production (Barracks Only)")]
        public List<GameObject> producibleUnitPrefabs;

        public class Baker : Baker<StructureAuthoring>
        {
            public override void Bake(StructureAuthoring authoring)
            {
                // 건물도 파괴되거나, 건설 중 애니메이션, 네트워킹을 위해 Dynamic 권장
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);

                // =======================================================================
                // 1. [정체성] 태그 부착 (Enum 대신 태그 사용)
                // =======================================================================
                AddComponent(entity, new StructureTag()); // "나는 건물이다" (공통)

                switch (authoring.structureType)
                {
                    case AuthoringStructureType.Wall:
                        AddComponent(entity, new WallTag());
                        // 벽은 자폭 기능이 있음
                        AddComponent(entity, new ExplosionData
                        {
                            Radius = authoring.explosionRadius,
                            Damage = authoring.explosionDamage,
                            Delay = authoring.explosionDelay
                        });
                        // 자폭 상태 (RemainingTime < 0이면 자폭 대기 아님)
                        AddComponent(entity, new SelfDestructTag
                        {
                            RemainingTime = -1f
                        });
                        break;
                    case AuthoringStructureType.Barracks:
                        AddComponent(entity, new BarracksTag());
                        // 배럭은 생산 대기열이 필요함
                        AddComponent(entity, new ProductionQueue
                        {
                            ProducingUnitIndex = -1,
                            Progress = 0,
                            Duration = 0,
                            IsActive = false
                        });
                        // 생산 가능 유닛 목록
                        var unitBuffer = AddBuffer<ProducibleUnitElement>(entity);
                        if (authoring.producibleUnitPrefabs != null)
                        {
                            for (int i = 0; i < authoring.producibleUnitPrefabs.Count; i++)
                            {
                                var prefab = authoring.producibleUnitPrefabs[i];
                                if (prefab != null)
                                {
                                    unitBuffer.Add(new ProducibleUnitElement
                                    {
                                        PrefabEntity = GetEntity(prefab, TransformUsageFlags.Dynamic),
                                        PrefabIndex = i
                                    });
                                }
                            }
                        }
                        break;
                    case AuthoringStructureType.Turret:
                        AddComponent(entity, new TurretTag());
                        break;
                    case AuthoringStructureType.ResourceCenter:
                        AddComponent(entity, new ResourceCenterTag());
                        break;
                }

                // =======================================================================
                // 2. [맵/위치] 그리드 정보
                // =======================================================================
                // 크기 (풋프린트)
                AddComponent(entity, new StructureFootprint
                {
                    Width = authoring.width,
                    Length = authoring.length,
                    Height = authoring.height
                });
                
                // 설치될 그리드 좌표
                AddComponent(entity, new GridPosition
                {
                    Position = int2.zero,
                });

                // =======================================================================
                // 3. [생산 정보] 건설 비용 및 시간 (Prefab Data)
                // =======================================================================
                // 유닛과 동일한 ProductionCost 사용 (통합됨)
                AddComponent(entity, new ProductionCost
                {
                    Cost = authoring.cost,
                    PopulationCost = 0
                });

                // 건설 소요 시간
                AddComponent(entity, new ProductionInfo
                {
                    ProductionTime = authoring.buildTime
                });

                // =======================================================================
                // 4. [기본 스탯] 초기값 주입
                // =======================================================================
                // 체력
                AddComponent(entity, new Health
                {
                    CurrentValue = authoring.maxHealth,
                    MaxValue = authoring.maxHealth
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
                
                // 건물은 MovementSpeed를 붙이지 않음 (움직이지 않으므로)

                // =======================================================================
                // 5. [전투] 공격 타워인 경우
                // =======================================================================
                if (authoring.structureType == AuthoringStructureType.Turret)
                {
                    
                    AddComponent(entity, new CombatStatus
                    {
                        AttackPower= authoring.attackDamage,
                        AttackSpeed = authoring.attackSpeed
                    });
                    
                    AddComponent(entity, new Reach
                    {
                        Value = authoring.attackRange,
                    });
                    
                    AddComponent(entity, new Target
                    {
                        TargetEntity = Entity.Null,
                    });
                }

                // =======================================================================
                // 6. [상태] 상태 머신 및 팀 정보
                // =======================================================================
                // 상태 동기화용 (Idle, Constructing, Destroyed 등)
                // 프리팹은 기본적으로 '완성품' 기준인 Idle로 둡니다.
                // * 실제 건설 시스템이 Instantiate 할 때 UnderConstruction 태그를 붙입니다.
                AddComponent(entity, new StructureState
                {
                    CurrentState = StructureContext.Idle
                });

                // 팀 ID
                AddComponent(entity, new Team { teamId = 0 });

                // 팀 컬러링
                AddComponent(entity, new URPMaterialPropertyBaseColor 
                { 
                    Value = new float4(1, 1, 1, 1) 
                });
            }
        }
    }
}