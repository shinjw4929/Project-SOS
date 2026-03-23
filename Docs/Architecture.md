# Project-SOS 아키텍처

## Project Overview

Project-SOS is a multiplayer RTS game built with Unity 6 (6000.0.64f1) using Unity's Data-Oriented Tech Stack (DOTS):
- **Entity Component System (ECS)** via Unity Entities 1.4.3
- Unity Physics 1.4.3 (standard Unity Rigidbody & Colliders auto-convert via Baking System)
- **Netcode for Entities** 1.10.0 for multiplayer synchronization
- **Client-Server Architecture** with authoritative server

## Build & Development

- **Unity Version**: 6000.0.64f1 | **Solution**: `Project-SOS.sln` (6 assemblies)
- **Editor Settings**: Enter Play Mode Settings > Do not reload Domain or Scene
- **Player Settings**: Run in Background (checked)
- **AutoConnect Port**: 7979 (defined in `GameBootStrap.cs`)

---

## Code Architecture

### Assembly Structure
```
Assets/Scripts/
├── Shared/          # Components, RPCs, systems used by both client & server
├── Client/          # Input handling, UI, visualization systems
├── Server/          # Server authority, game logic enforcement
└── Authoring/       # GameObject → Entity conversion (baking)
```

### Folder Structure & Naming Patterns

**상세 파일 목록**: [코드베이스 구조.md](Systems/코드베이스%20구조.md)

| 폴더 | 역할 | 네이밍 패턴 |
|------|------|-------------|
| `Client/Component/Singleton/` | 클라이언트 싱글톤 | `*State.cs` |
| `Client/Systems/Commands/` | 입력 처리 시스템 | `*InputSystem.cs` |
| `Server/Systems/Commands/` | RPC 처리 시스템 | `Handle*RequestSystem.cs` |
| `Server/Systems/Combat/` | 전투 로직 | `*AttackSystem.cs`, `*DamageSystem.cs` |
| `Shared/Components/Tags/` | 태그 컴포넌트 | `*Tag.cs` |
| `Shared/RPCs/` | 네트워크 RPC | `*Rpc.cs`, `*RequestRpc.cs` |
| `Authoring/` | Baking 컴포넌트 | `*Authoring.cs` |

---

## System Execution Flow

```
[1. 입력] GhostInputSystemGroup (Client)
    UserSelectionInputUpdateSystem → EntitySelectionSystem → SelectedEntityInfoUpdateSystem
        ├─ UnitCommandInputSystem → RPC 전송 (MoveRequestRpc/AttackRequestRpc)
        │      ↓
        │  StructurePlacementInputSystem → RPC 전송 (BuildRequestRpc/BuildMoveRequestRpc)
        └─ StructureCommandInputSystem

[2. 공간 분할] SpatialPartitioningGroup (Server, OrderFirst)
    SpatialMapBuildSystem → TargetingMap (10.0f) + MovementMap (3.0f) → SpatialMaps 싱글톤

[3. 명령 처리] SimulationSystemGroup (Server)
    Handle*RequestSystem → MovementGoal, AggroTarget, Intent 설정

[4. 타겟팅] SimulationSystemGroup (Server)
    UnifiedTargetingSystem (SpatialMaps.TargetingMap 사용)
        ├─ EnemyTargetJob → 적→아군 타겟팅
        └─ UnitAutoTargetJob → 유닛→적 자동 감지

[5. 이동] SimulationSystemGroup (Server)
    GridObstacleResponseSystem → GridObstacleCleanupSystem
    → FlowFieldSystem (Phase0→Collect→Compute×8→Apply) → FlowFieldSteeringSystem
    → PredictedMovementSystem (SpatialMaps.MovementMap + GridCell.IsPathBlocked 사용)
    → MovementArrivalSystem → BuildArrivalSystem (도착 시 건설)

[6. 전투] FixedStepSimulationSystemGroup (Server)
    CombatDamageSystem → MeleeAttackSystem → RangedAttackSystem → DamageApplySystem
    ※ 모든 데미지는 DamageEvent 버퍼를 통해 DamageApplySystem에서 일괄 적용 + 적 킬 카운트

[7. 정리] SimulationSystemGroup (Server)
    HeroDeathDetectionSystem → ServerDeathSystem → GridObstacleCleanupSystem, TechStateRecalculateSystem

[7.5. 네트워크 최적화] SimulationSystemGroup (Server)
    UpdateConnectionPositionSystem (CameraPositionRpc 수신) → GhostRelevancySystem (뷰포트 AABB × 1.3 밖 적/다른 유저 유닛 Ghost 전송 제외, 자기 유닛 항상 relevant)
    MinimapDataBroadcastSystem → MinimapBatchRpc 분산 전송 (적/유닛 위치+teamId, 단일 float3 파이프라인)

[7.6. 미니맵] SimulationSystemGroup (Client)
    MinimapDataReceiveSystem → MinimapDataState (Double buffer 스왑, 적/유닛 수 카운트)
    MinimapRenderer (MonoBehaviour) → Texture2D 렌더링

[8. 후처리] LateSimulationSystemGroup
    GridOccupancyEventSystem, PopulationApplySystem (인구수 변경 이벤트 소비)

[9. Transform] TransformSystemGroup
    CarriedResourceFollowSystem (Scale 기반 가시성 토글)

[9.5. 커맨드 마커] SimulationSystemGroup (Client)
    CommandMarkerPoolInitSystem → 타입당 4개 풀 초기화 (1회 실행)
    CommandMarkerFadeSystem → Scale 선형 감소 + 수명 만료 시 Scale=0 (풀링)

[10. 렌더링] PresentationSystemGroup (Client)
    StructurePreviewUpdateSystem, EnemyHpTextPresentationSystem, CameraSystem
```

**핵심 의존성**:
- `UnifiedTargetingSystem`: UpdateAfter `SpatialPartitioningGroup`, `HandleAttackRequestSystem`
- `DamageApplySystem`: UpdateAfter `MeleeAttackSystem` (DamageEvent 버퍼 소비)
- `FlowFieldSystem`: BFS 기반 Flow Field 계산 (LRU 캐시 32×2풀, 8 IJob 병렬), Grid 기반 passability
- `SpatialMapBuildSystem`: Persistent 맵 + Job 기반 Clear → dependency chain으로 동기화 (CompleteDependency 불필요)
- `GhostRelevancySystem`: UpdateAfter `UpdateConnectionPositionSystem` (Ghost Relevancy AABB 필터링, 적+다른 유저 유닛, 자기 유닛 skip, ViewHalfExtent × 1.3/1.15)

---

## Key Patterns

### 1. DamageEvent Buffer Pattern (필수)
Health를 여러 시스템에서 직접 수정하면 Job 스케줄링 충돌이 발생한다. **DamageEvent 버퍼**를 사용한다.
```csharp
// ❌ 잘못된 방법: Health 직접 수정 → Job 충돌!
var health = _healthLookup[targetEntity];
health.CurrentValue -= damage;
_healthLookup[targetEntity] = health;

// ✅ 올바른 방법: DamageEvent 버퍼에 추가
if (_damageEventLookup.HasBuffer(targetEntity))
{
    var buffer = _damageEventLookup[targetEntity];
    buffer.Add(new DamageEvent { Damage = finalDamage });
}
// DamageApplySystem이 나중에 버퍼를 읽어서 Health에 적용
```

### 2. Authoring Composition Pattern
| 프리팹 | Authoring 조합 |
|--------|----------------|
| 유닛 (Hero, Worker 등) | `MovementAuthoring` + `UnitMovementAuthoring` + `UnitAuthoring` |
| 적 (Enemy) | `MovementAuthoring` + `EnemyAuthoring` |
| 건물 (Wall, Barracks 등) | `StructureAuthoring` |

### 3. User State Machine
```csharp
public enum UserContext : byte {
    Command = 0,              // 기본 명령 상태
    BuildMenu = 1,            // 건설 메뉴 (빌더 Q)
    Construction = 2,         // 건물 배치 모드
    StructureActionMenu = 10, // 생산 메뉴 (건물 Q)
    Dead = 255,               // 사망/게임오버
}
```

### 4. Work Range Pattern (작업 거리 계산)
모든 작업(채집, 건설, 전투)의 상호작용 거리는 **타겟 표면 기준**으로 계산한다. 공통 로직은 `ArrivalUtility`(`Shared/Utilities/ArrivalUtility.cs`)에 집약되어 있다.
```csharp
// 채집/건설: 도착 거리 = 타겟 반지름 + WorkRange (ArrivalUtility.GetInteractionArrivalDistance)
float arrivalDistance = ArrivalUtility.GetInteractionArrivalDistance(targetRadius, workRange);

// 접근점 계산: 타겟 표면까지의 이동 목표 (ArrivalUtility.CalculateApproachPoint)
float3 approachPos = ArrivalUtility.CalculateApproachPoint(fromPos, targetPos, targetEntity, in radiusLookup);

// Dead Zone 방지: ArrivalRadius 설정 (ArrivalUtility.GetSafeArrivalRadius)
float arrivalRadius = ArrivalUtility.GetSafeArrivalRadius(workRange);

// 전투: 유효 거리 = 직선 거리 - 타겟 반지름 (CombatUtility)
float effectiveDistance = rawDistance - targetRadius;
bool inRange = effectiveDistance <= attackRange;
```
- **공격자/작업자의 반지름 사용 안 함**: 타겟 표면까지의 거리만 계산
- **WorkRange/AttackRange**: 프리팹 인스펙터에서 조정 가능 (UnitAuthoring.workRange)
- **일관성**: 채집/건설 시스템은 `ArrivalUtility`를 공유, 전투는 `CombatUtility` 사용

### 5. Other Patterns (간략)
- **Selection System**: Phase 기반 (`UserSelectionInputState.Phase`) → `EntitySelectionSystem`에서 Selected 토글
- **Combat Flow**: MeleeAttackSystem/RangedAttackSystem → DamageEvent 버퍼 → DamageApplySystem
- **CarriedResource Visibility**: Scale 토글 (`CarriedAmount > 0 ? 1f : 0f`) - Structural Change 없음
- **Spatial Partitioning**: `SpatialMapBuildSystem`에서 Persistent 맵 Clear + 재빌드 → 사용 시스템에서 ReadOnly → Job dependency chain으로 동기화
- **Catalog Patterns**: UnitCatalog/StructureCatalog(버퍼) vs EnemyPrefabCatalog(명시적 필드)

### 6. Network RPCs
`MoveRequestRpc`, `AttackRequestRpc`, `BuildRequestRpc`, `BuildMoveRequestRpc`, `GatherRequestRpc`, `ReturnResourceRequestRpc`, `ProduceUnitRequestRpc`, `SelfDestructRequestRpc`, `CameraPositionRpc`, `NotificationRpc`, `HeroDeathRpc`, `GameOverRpc`, `MinimapBatchRpc`

---

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

---

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
| Job 구조체 | OnUpdate에서 읽어 필드 전달 | `new MyJob { SeparationStrength = gs.SeparationStrength }` |
| Utility static 메서드 | 기본값 파라미터 | `CheckStuck(..., float interval = DefaultInterval)` |
| Baker | Authoring 필드 사용 (bake 시 GameSettings 미존재) | 주석으로 GameSettings 동기화 표시 |

### GameSettings 카테고리

- **경제**: InitialCurrency, InitialMaxPopulation
- **건설**: ResourceNodeExclusionDistance, MaxBuildRetryCount, UnitSpawnOffset, DefaultProductionTime
- **전투/AI**: AggroLockDuration, TargetHysteresisMultiplier, TargetSearchInterval
- **이동**: SeparationStrength, SeparationPadding, SeparationForceCurve
- **적 AI**: StuckCheckInterval, StuckThreshold, DormantMinDuration, DormantMaxDuration
- **장애물**: PathInvalidationRadius, PartialPathInvalidationRadius
- **스폰**: EnemyBigSpawnRate, EnemySmallOnlyRate, Wave0SpawnSpacing, PeriodicSpawnSpacing
- **Wave**: Wave0InitialSpawnCount, Wave1/2 TriggerTime/KillCount/SpawnInterval/SpawnCount, MaxEnemyCount
