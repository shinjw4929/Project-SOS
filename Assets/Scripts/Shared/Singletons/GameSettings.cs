using Unity.Entities;

namespace Shared
{
    /// <summary>
    /// 게임 운영 관련 설정 싱글톤.
    /// 씬에 GameSettingsAuthoring을 배치하여 설정값 관리.
    /// </summary>
    public struct GameSettings : IComponentData
    {
        /// <summary>
        /// 초기 배치 벽이 자동 파괴되기까지 걸리는 시간 (초)
        /// </summary>
        public float InitialWallDecayTime;

        // === Wave 설정 ===

        /// <summary>
        /// Wave0 초기 스폰 적 수 (기본값: 30)
        /// </summary>
        public int Wave0InitialSpawnCount;

        /// <summary>
        /// Wave1 전환 시간 (초) (기본값: 60)
        /// </summary>
        public float Wave1TriggerTime;

        /// <summary>
        /// Wave1 전환 처치 수 (기본값: 15)
        /// </summary>
        public int Wave1TriggerKillCount;

        /// <summary>
        /// Wave2 전환 시간 (초) (기본값: 120)
        /// </summary>
        public float Wave2TriggerTime;

        /// <summary>
        /// Wave2 전환 처치 수 (기본값: 30)
        /// </summary>
        public int Wave2TriggerKillCount;

        /// <summary>
        /// Wave1 적 스폰 주기 (초) (기본값: 5)
        /// </summary>
        public float Wave1SpawnInterval;

        /// <summary>
        /// Wave1 1회 스폰 수 (기본값: 3)
        /// </summary>
        public int Wave1SpawnCount;

        /// <summary>
        /// Wave2 적 스폰 주기 (초) (기본값: 4)
        /// </summary>
        public float Wave2SpawnInterval;

        /// <summary>
        /// Wave2 1회 스폰 수 (기본값: 4)
        /// </summary>
        public int Wave2SpawnCount;

        /// <summary>
        /// 맵에 존재할 수 있는 최대 적 수 (기본값: 1200)
        /// </summary>
        public int MaxEnemyCount;

        // === 건설 규칙 ===

        /// <summary>ResourceCenter가 자원 노드로부터 떨어져야 하는 최소 거리 (그리드 셀 단위)</summary>
        public int ResourceNodeExclusionDistance;
        /// <summary>건설 도착 재시도 최대 횟수</summary>
        public int MaxBuildRetryCount;
        /// <summary>유닛 생산 시 건물 가장자리로부터 스폰 오프셋 (월드 단위, m)</summary>
        public float UnitSpawnOffset;
        /// <summary>ProductionInfo 없을 때 기본 생산 시간 (초)</summary>
        public float DefaultProductionTime;

        // === 경제 ===

        /// <summary>게임 시작 시 플레이어 초기 자원</summary>
        public int InitialCurrency;
        /// <summary>게임 시작 시 최대 인구 제한</summary>
        public int InitialMaxPopulation;

        // === 전투/AI ===

        /// <summary>피격 시 어그로 고정 지속 시간 (초)</summary>
        public float AggroLockDuration;
        /// <summary>타겟 이탈 판정 배수 (VisionRange × 이 값 = 이탈 거리)</summary>
        public float TargetHysteresisMultiplier;
        /// <summary>적/유닛 타겟 탐색 주기 (N프레임에 1회)</summary>
        public uint TargetSearchInterval;

        // === 이동 ===

        /// <summary>Steering 회피 블렌딩 강도 (높을수록 강한 회피)</summary>
        public float AvoidanceStrength;
        /// <summary>회피 거리 추가 패딩 (월드 단위, m)</summary>
        public float AvoidancePadding;

        // === 경로탐색 ===

        /// <summary>프레임당 최대 FlowField BFS 계산 수 (캐시 miss 폭주 방지)</summary>
        public int MaxBFSPerFrame;
        /// <summary>Steering 회피 계산 분산 주기 (N프레임에 1회, 나머지는 캐시 재사용)</summary>
        public uint SteeringSliceDivisor;

        // === 배회 ===

        /// <summary>Wandering 방향 맵 중심 편향 (0=랜덤, 1=중심 직진)</summary>
        public float WanderBiasFactor;
        /// <summary>Wandering 최대 이동 거리 (m)</summary>
        public float WanderMaxDistance;

        // === 타겟 전파 ===

        /// <summary>타겟 전파 반경 (적이 주변 적의 타겟을 복사하는 범위, m)</summary>
        public float TargetPropagationRadius;

        // === 적 AI ===

        /// <summary>위치 정체 체크 간격 (초)</summary>
        public float StuckCheckInterval;
        /// <summary>정체 판정 이동 거리 (월드 단위, m)</summary>
        public float StuckThreshold;
        /// <summary>Dormant 최소 지속 시간 (초)</summary>
        public float DormantMinDuration;
        /// <summary>Dormant 최대 지속 시간 (초)</summary>
        public float DormantMaxDuration;
        /// <summary>도달 불가 타겟 차단 지속 시간 (초)</summary>
        public float TargetAbandonDuration;

        // === 장애물 ===

        /// <summary>건물 건설 시 주변 유닛 경로 무효화 반지름 (월드 단위, m)</summary>
        public float PathInvalidationRadius;
        /// <summary>건물 파괴 시 Partial Path 유닛 경로 무효화 반지름 (월드 단위, m)</summary>
        public float PartialPathInvalidationRadius;

        // === 스폰 ===

        /// <summary>Wave2 적 스폰 시 Big 비율 (Flying 있을 때, 0~1)</summary>
        public float EnemyBigSpawnRate;
        /// <summary>Wave2 적 스폰 시 Small 비율 (Flying 없을 때, 0~1)</summary>
        public float EnemySmallOnlyRate;
        /// <summary>Wave0 초기 스폰 그리드 간격 (월드 단위, m)</summary>
        public float Wave0SpawnSpacing;
        /// <summary>주기적 스폰 원형 배치 간격 (월드 단위, m)</summary>
        public float PeriodicSpawnSpacing;

        // === 애니메이션 ===

        /// <summary>전투 기울임 각도 (라디안, 기본 0.3 ≈ 17도)</summary>
        public float CombatTiltAngle;
        /// <summary>전투 기울임 보간 속도 (높을수록 빠름)</summary>
        public float CombatTiltSpeed;
    }
}
