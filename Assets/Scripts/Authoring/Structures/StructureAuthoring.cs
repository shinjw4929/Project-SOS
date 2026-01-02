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
        // 인스펙터용 Enum (ECS 로직에는 안 들어가고, 베이킹 분기용으로만 사용)
        public enum AuthoringStructureType
        {
            Wall,           // 벽
            Barracks,       // 공격 유닛 생산
            Turret,         // 공격 타워
            ResourceCenter  // 자원 반납, 일꾼 유닛 생산
        }
        
        [Header("Identity")]
        public AuthoringStructureType structureType = AuthoringStructureType.Wall;

        [Header("Unit Production Settings")]
        public List<GameObject> producibleUnits; // 이 건물이 생산할 수 있는 유닛 프리팹 목록
        
        [Header("Grid Size")]
        [Min(1)] public int width = 1;
        [Min(1)] public int length = 1;
        public float height = 1;
        
        [Header("Build Info (Cost & Time)")]
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
        
        [Header("Self-Destruct")]
        public float explosionRadius = 3.0f;
        public float explosionDamage = 100.0f;
        public float explosionDelay = 0.5f;
        
        public class Baker : Baker<StructureAuthoring>
        {
            public override void Bake(StructureAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);

                // =======================================================================
                // 1. [정체성] 태그 및 역할 플래그 설정
                // =======================================================================
                AddComponent(entity, new StructureTag());
                AddComponent(entity, new Team { teamId = 0 });

                // 역할 플래그 (베이킹 로직 분기용)
                bool canProduce  = false;
                bool canAttack      = false;
                bool canExplode     = false;
                
                switch (authoring.structureType)
                {
                    case AuthoringStructureType.Wall:
                        AddComponent(entity, new WallTag());
                        canExplode = true; 
                        break;
                        
                    case AuthoringStructureType.Barracks:
                        AddComponent(entity, new ProductionFacilityTag());
                        canProduce = true;
                        break;
                        
                    case AuthoringStructureType.Turret:
                        AddComponent(entity, new TurretTag());
                        canAttack = true;
                        break;
                        
                    case AuthoringStructureType.ResourceCenter:
                        AddComponent(entity, new ProductionFacilityTag());
                        canProduce = true;
                        break;
                }

                // =======================================================================
                // 2. [기능] 조건부 컴포넌트 부착 (옵션)
                // =======================================================================
                
                // A. 유닛 생산 능력 (Barracks)
                if (canProduce)
                {
                    // 생산 큐 상태
                    AddComponent(entity, new ProductionQueue
                    {
                        ProducingUnitIndex = -1,
                        Progress = 0,
                        Duration = 0,
                        IsActive = false
                    });

                    // 생산 목록 버퍼 생성 (ProductionOption)
                    if (authoring.producibleUnits != null && authoring.producibleUnits.Count > 0)
                    {
                        var buffer = AddBuffer<AvailableUnit>(entity);
                        foreach (var unitObj in authoring.producibleUnits)
                        {
                            if (unitObj == null) continue;
                            
                            buffer.Add(new AvailableUnit
                            {
                                PrefabEntity = GetEntity(unitObj, TransformUsageFlags.Dynamic)
                            });
                        }
                    }
                }
                
                // B. 전투 능력 (Turret)
                if (canAttack)
                {
                    if (authoring.attackDamage > 0)
                    {
                        AddComponent(entity, new CombatStatus
                        {
                            AttackPower = authoring.attackDamage,
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
                        
                        // 발사체 발사 기능이 필요하다면 추가
                        AddComponent<ProjectileFireInput>(entity);
                    }
                }

                // C. 자폭 능력 (Wall)
                if (canExplode)
                {
                    AddComponent(entity, new ExplosionData
                    {
                        Radius = authoring.explosionRadius,
                        Damage = authoring.explosionDamage,
                        Delay = authoring.explosionDelay
                    });
                    
                    AddComponent(entity, new SelfDestructTag
                    {
                        RemainingTime = -1f // 대기 상태
                    });
                }
                
                // =======================================================================
                // 3. [기본 스탯] 공통 데이터
                // =======================================================================
                
                // 크기 (풋프린트)
                AddComponent(entity, new StructureFootprint
                {
                    Width = authoring.width,
                    Length = authoring.length,
                    Height = authoring.height
                });
                
                // 그리드 위치
                AddComponent(entity, new GridPosition { Position = int2.zero });

                // 건설 비용 및 시간
                AddComponent(entity, new ProductionCost
                {
                    Cost = authoring.cost,
                    PopulationCost = 0 // 건물은 인구수 소모 없음
                });

                // 건설 소요 시간
                AddComponent(entity, new ProductionInfo
                {
                    ProductionTime = authoring.buildTime
                });
                
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
                
                // =======================================================================
                // 4. [상태 및 렌더링]
                // =======================================================================
                // 건물 상태 (Idle, Constructing, Destroyed 등)
                AddComponent(entity, new StructureState
                {
                    CurrentState = StructureContext.Idle
                });
                
                // 팀 컬러링
                AddComponent(entity, new URPMaterialPropertyBaseColor
                {
                    Value = new float4(1, 1, 1, 1)
                });

                // =======================================================================
                // 5. [NavMesh Obstacle] 경로 탐색 장애물 (서버 전용)
                // =======================================================================
                // NavMesh Obstacle 경로 탐색 장애물 (서버 전용)
                // Managed Component는 Runtime에서 추가됨
                AddComponent(entity, new NeedsNavMeshObstacle());
                SetComponentEnabled<NeedsNavMeshObstacle>(entity, false); // 초기 비활성화
            }
        }
    }
}