using Unity.Entities;
using UnityEngine;
using Shared;

namespace Authoring
{
    /// <summary>
    /// 게임 운영 설정 Authoring.
    /// EntitiesSubScene에 빈 GameObject를 만들고 이 컴포넌트를 추가하여 사용.
    /// </summary>
    public class GameSettingsAuthoring : MonoBehaviour
    {
        [Header("Initial Wall Settings")]
        [Tooltip("초기 배치 벽이 자동 파괴되기까지 걸리는 시간 (초)")]
        [Min(0f)]
        public float initialWallDecayTime = 30f;

        [Header("Wave0 Settings")]
        [Tooltip("게임 시작 시 초기 스폰할 적 수")]
        [Min(0)]
        public int wave0InitialSpawnCount = 30;

        [Header("Wave Transition Conditions")]
        [Tooltip("Wave1 전환 경과 시간 (초)")]
        [Min(1f)]
        public float wave1TriggerTime = 60f;
        [Tooltip("Wave1 전환 처치 수")]
        [Min(1)]
        public int wave1TriggerKillCount = 15;
        [Tooltip("Wave2 전환 경과 시간 (초)")]
        [Min(1f)]
        public float wave2TriggerTime = 120f;
        [Tooltip("Wave2 전환 처치 수")]
        [Min(1)]
        public int wave2TriggerKillCount = 30;

        [Header("Enemy Limit")]
        [Tooltip("맵에 존재할 수 있는 최대 적 수")]
        [Min(1)]
        public int maxEnemyCount = 1200;

        [Header("Construction Rules")]
        [Tooltip("ResourceCenter가 자원 노드로부터 떨어져야 하는 최소 거리 (그리드 셀 단위)")]
        [Min(0)]
        public int resourceNodeExclusionDistance = 9;
        [Tooltip("건설 도착 재시도 최대 횟수")]
        [Min(1)]
        public int maxBuildRetryCount = 3;
        [Tooltip("유닛 생산 시 건물 가장자리로부터 스폰 오프셋 (m)")]
        [Min(0f)]
        public float unitSpawnOffset = 1.0f;
        [Tooltip("ProductionInfo 없을 때 기본 생산 시간 (초)")]
        [Min(0.1f)]
        public float defaultProductionTime = 5f;

        [Header("Economy")]
        [Tooltip("게임 시작 시 플레이어 초기 자원")]
        [Min(0)]
        public int initialCurrency = 100;
        [Tooltip("게임 시작 시 최대 인구 제한")]
        [Min(1)]
        public int initialMaxPopulation = 300;

        [Header("Combat / AI")]
        [Tooltip("피격 시 어그로 고정 지속 시간 (초)")]
        [Min(0.1f)]
        public float aggroLockDuration = 3.0f;
        [Tooltip("타겟 이탈 판정 배수 (VisionRange × 이 값)")]
        [Min(1f)]
        public float targetHysteresisMultiplier = 1.3f;
        [Tooltip("적/유닛 타겟 탐색 주기 (N프레임에 1회)")]
        [Min(1)]
        public uint targetSearchInterval = 4;

        [Header("Movement")]
        [Tooltip("충돌 회피 힘 배수")]
        [Min(0f)]
        public float separationStrength = 4.0f;
        [Tooltip("분리 거리 추가 패딩 (m)")]
        [Min(0f)]
        public float separationPadding = 0.3f;
        [Tooltip("침투 깊이 비례 힘 곡선 배수")]
        [Min(0f)]
        public float separationForceCurve = 3.0f;

        [Header("Enemy AI")]
        [Tooltip("위치 정체 체크 간격 (초)")]
        [Min(0.5f)]
        public float stuckCheckInterval = 3.0f;
        [Tooltip("정체 판정 이동 거리 (m)")]
        [Min(0.1f)]
        public float stuckThreshold = 2.0f;
        [Tooltip("Dormant 최소 지속 시간 (초)")]
        [Min(0f)]
        public float dormantMinDuration = 5.0f;
        [Tooltip("Dormant 최대 지속 시간 (초)")]
        [Min(0f)]
        public float dormantMaxDuration = 8.0f;

        [Header("Obstacle")]
        [Tooltip("건물 건설 시 주변 유닛 경로 무효화 반지름 (m)")]
        [Min(1f)]
        public float pathInvalidationRadius = 8f;
        [Tooltip("건물 파괴 시 Partial Path 무효화 반지름 (m)")]
        [Min(1f)]
        public float partialPathInvalidationRadius = 12f;

        [Header("Spawn Balance")]
        [Tooltip("Wave2 적 스폰 시 Big 비율 (Flying 있을 때, 0~1)")]
        [Range(0f, 1f)]
        public float enemyBigSpawnRate = 0.85f;
        [Tooltip("Wave2 적 스폰 시 Small 비율 (Flying 없을 때, 0~1)")]
        [Range(0f, 1f)]
        public float enemySmallOnlyRate = 0.60f;
        [Tooltip("Wave0 초기 스폰 그리드 간격 (m)")]
        [Min(0.5f)]
        public float wave0SpawnSpacing = 1f;
        [Tooltip("주기적 스폰 원형 배치 간격 (m)")]
        [Min(0.5f)]
        public float periodicSpawnSpacing = 3f;

        [Header("Animation")]
        [Tooltip("전투 기울임 각도 (라디안, 0.3 ≈ 17도)")]
        [Min(0f)]
        public float combatTiltAngle = 0.3f;
        [Tooltip("전투 기울임 보간 속도 (높을수록 빠름)")]
        [Min(0.1f)]
        public float combatTiltSpeed = 8.0f;

        [Header("Wave1+ Spawn Settings")]
        [Tooltip("Wave1 적 스폰 주기 (초)")]
        [Min(0.5f)]
        public float wave1SpawnInterval = 5f;
        [Tooltip("Wave1 1회 스폰 수")]
        [Min(1)]
        public int wave1SpawnCount = 3;
        [Tooltip("Wave2 적 스폰 주기 (초)")]
        [Min(0.5f)]
        public float wave2SpawnInterval = 4f;
        [Tooltip("Wave2 1회 스폰 수")]
        [Min(1)]
        public int wave2SpawnCount = 4;

        public class Baker : Baker<GameSettingsAuthoring>
        {
            public override void Bake(GameSettingsAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new GameSettings
                {
                    InitialWallDecayTime = authoring.initialWallDecayTime,
                    Wave0InitialSpawnCount = authoring.wave0InitialSpawnCount,
                    Wave1TriggerTime = authoring.wave1TriggerTime,
                    Wave1TriggerKillCount = authoring.wave1TriggerKillCount,
                    Wave2TriggerTime = authoring.wave2TriggerTime,
                    Wave2TriggerKillCount = authoring.wave2TriggerKillCount,
                    Wave1SpawnInterval = authoring.wave1SpawnInterval,
                    Wave1SpawnCount = authoring.wave1SpawnCount,
                    Wave2SpawnInterval = authoring.wave2SpawnInterval,
                    Wave2SpawnCount = authoring.wave2SpawnCount,
                    MaxEnemyCount = authoring.maxEnemyCount,
                    ResourceNodeExclusionDistance = authoring.resourceNodeExclusionDistance,
                    MaxBuildRetryCount = authoring.maxBuildRetryCount,
                    UnitSpawnOffset = authoring.unitSpawnOffset,
                    DefaultProductionTime = authoring.defaultProductionTime,
                    InitialCurrency = authoring.initialCurrency,
                    InitialMaxPopulation = authoring.initialMaxPopulation,
                    AggroLockDuration = authoring.aggroLockDuration,
                    TargetHysteresisMultiplier = authoring.targetHysteresisMultiplier,
                    TargetSearchInterval = authoring.targetSearchInterval,
                    SeparationStrength = authoring.separationStrength,
                    SeparationPadding = authoring.separationPadding,
                    SeparationForceCurve = authoring.separationForceCurve,
                    StuckCheckInterval = authoring.stuckCheckInterval,
                    StuckThreshold = authoring.stuckThreshold,
                    DormantMinDuration = authoring.dormantMinDuration,
                    DormantMaxDuration = authoring.dormantMaxDuration,
                    PathInvalidationRadius = authoring.pathInvalidationRadius,
                    PartialPathInvalidationRadius = authoring.partialPathInvalidationRadius,
                    EnemyBigSpawnRate = authoring.enemyBigSpawnRate,
                    EnemySmallOnlyRate = authoring.enemySmallOnlyRate,
                    Wave0SpawnSpacing = authoring.wave0SpawnSpacing,
                    PeriodicSpawnSpacing = authoring.periodicSpawnSpacing,
                    CombatTiltAngle = authoring.combatTiltAngle,
                    CombatTiltSpeed = authoring.combatTiltSpeed
                });
            }
        }
    }
}
