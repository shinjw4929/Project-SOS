# 게임 규칙 및 설정

## Prefabs & Scenes

```
Assets/Prefabs/
├── Enemy/           EnemySmall, EnemyBig, EnemyFlying (isRanged=true)
├── Units/           Hero, Worker, Striker, Archer, Tank
├── Structures/      Wall, Barracks, ResourceCenter
├── Economies/       Cheese (운반 자원), UserEconomy
├── Shoot/           Projectile
└── UI/              CommandButton, SelectionRing*, EnemyHPText3D

Assets/Scenes/
├── InGame.unity              # 메인 게임플레이 씬
└── InGame/EntitiesSubScene.unity  # DOTS 엔티티 서브씬
```

## Game Design

### Wave System
| Wave | 전환 조건 | 스폰 |
|------|-----------|------|
| 0 | 게임 시작 | EnemyBig 30마리 즉시 |
| 1 | 60초 OR 15처치 | 5초마다 3마리 (Small 60%, Big 40%) |
| 2 | 120초 OR 30처치 | 4초마다 4마리 (Small 50%, Big 35%, Flying 15%) |

**적 타입**: EnemySmall(빠름/근접), EnemyBig(강함/근접), EnemyFlying(공중/원거리/벽무시)

### User Input
- **유닛 선택**: 좌클릭 드래그(다수), 단일 클릭(단일)
- **유닛 이동**: 우클릭 → MoveRequestRpc
- **공격**: 적 우클릭 → AttackRequestRpc
- **건설**: 빌더 선택 → Q → 건물 선택 → 배치 → BuildRequestRpc/BuildMoveRequestRpc
- **생산**: 생산 시설 선택 → Q → 유닛 선택 → ProduceUnitRequestRpc

---

## Collider 역할 규칙

### 용도 제한
- Collider는 **raycast (선택, 건설 검증) + 투사체 충돌** 전용
- 물리 충돌 (벽 미끄러짐, 건설 시 push-out)은 **그리드 기반** — Collider 사용 금지

### 크기 정합성
- **유닛/적**: Capsule/Sphere Collider 반지름 ≈ ObstacleRadius (raycast 히트 영역)
- **건물/자원**: Box Collider 크기 ≈ Width × Length × CellSize (raycast 히트 영역)
- Collider는 자동 베이킹 (코드 생성 금지)
- 크기 정합성은 프리팹 설정 시 수동 확인

### 물리 충돌 = 그리드 단일 소스
- 건물 크기: StructureFootprint.Width/Length (그리드 셀 단위)
- 경로 차단: max(1, Width-2) × max(1, Length-2) (중앙 영역)
- Push-out: Width × CellSize / 2 (직사각형, 항상)
- 벽 미끄러짐: GridCell.IsPathBlocked 셀 경계 (PredictedMovementSystem)

---

## StructureFootprint 필드 매핑

| 필드 | 단위 | 참조 시스템 | 용도 |
|------|------|-----------|------|
| Width | 그리드 셀 | GridOccupancyEventSystem, ObstacleGridInitSystem, HandleBuildRequestSystem, BuildArrivalSystem, GridObstacleResponseSystem, GridObstacleCleanupSystem, InitialWallDecaySystem, ProductionProgressSystem | 배치 점유, 경로 차단 (W-2 파생), push-out (W×CellSize 파생) |
| Length | 그리드 셀 | 동일 | 동일 |
| Height | 월드 단위 (m) | BuildArrivalSystem | 건물 높이 (위치 계산) |

파생값은 StructureFootprint 필드가 아닌, 시스템에서 인라인 계산된다:
- 경로 차단 폭: `math.max(1, Width - 2)` — GridObstacleResponseSystem, ObstacleGridInitSystem 등
- Push-out 반폭: `Width * CellSize * 0.5f` — GridObstacleResponseSystem

---

## GameSettings 밸런스 설정 패턴

모든 게임 밸런스/규칙 상수는 `GameSettings` 싱글톤으로 관리한다. 시스템 코드에 하드코딩 금지.

### 접근 패턴

| 컨텍스트 | 패턴 | 예시 |
|---------|------|------|
| Main Thread (OnUpdate) | `TryGetSingleton` + fallback | `SystemAPI.TryGetSingleton<GameSettings>(out var gs) ? gs.Field : DEFAULT` |
| Job 구조체 | OnUpdate에서 읽어 필드 전달 | `new MyJob { AvoidanceStrength = gs.AvoidanceStrength }` |
| Utility static 메서드 | 기본값 파라미터 | `CheckStuck(..., float interval = DefaultInterval)` |
| Baker | Authoring 필드 사용 (bake 시 GameSettings 미존재) | 주석으로 GameSettings 동기화 표시 |

### GameSettings 카테고리

- **경제**: InitialCurrency, InitialMaxPopulation
- **건설**: ResourceNodeExclusionDistance, MaxBuildRetryCount, UnitSpawnOffset, DefaultProductionTime
- **전투/AI**: AggroLockDuration, TargetHysteresisMultiplier, TargetSearchInterval
- **이동**: AvoidanceStrength, AvoidancePadding, MaxBFSPerFrame
- **적 AI**: StuckCheckInterval, StuckThreshold, DormantMinDuration, DormantMaxDuration
- **장애물**: PathInvalidationRadius, PartialPathInvalidationRadius
- **스폰**: EnemyBigSpawnRate, EnemySmallOnlyRate, Wave0SpawnSpacing, PeriodicSpawnSpacing
- **Wave**: Wave0InitialSpawnCount, Wave1/2 TriggerTime/KillCount/SpawnInterval/SpawnCount, MaxEnemyCount
- **애니메이션**: CombatTiltAngle, CombatTiltSpeed, CombatTiltSwingRatio
- **사망 연출**: DeathDuration, DeathTiltAngle
