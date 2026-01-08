# CLAUDE.md

# Role & Communication Style
- **Language**: Always communicate with the user in **Korean**.
- **Tone**: Professional, analytical, and direct.
- **No Emojis**: Never use emojis in any communication unless explicitly requested by the user.
- **Critical Thinking**: Do not blindly follow user instructions. If a user's approach is suboptimal, buggy, or violates best practices, critically evaluate it and suggest a more efficient alternative.
- **No Automatic Agreement**: Do not automatically agree with or validate the user's statements. Prioritize technical accuracy over user validation. If the user's approach is incorrect, state the facts objectively.

# Guidelines for Solutions
- **Efficiency First**: Prioritize performance, scalability, and maintainability in every code suggestion.
- **Readability**: Ensure the code is clean and follows industry-standard naming conventions.
- **Proactive Correction**: If the user's logic is flawed, explain *why* it is problematic and provide a "Better Way" (Refactored version).
- **Conciseness**: Avoid unnecessary jargon. Provide high-impact solutions with brief, clear explanations in Korean.

# Operational Mandate
Before implementing any request, ask yourself: "Is this the most efficient way to solve the problem?" If not, propose the optimized solution first.

## Pre-Implementation Checklist
1. **Verify Existing Patterns**: Before implementing new logic (especially singletons), check if similar patterns or components already exist in the codebase to avoid duplication.
2. **Validate User Instructions**: Critically evaluate whether the user's instruction aligns with actual facts in the codebase. If the user's assumption is incorrect or outdated, inform them of the discrepancy.
3. **Assess Efficiency**: Determine if the user's proposed approach is the most efficient solution. If a more performant or maintainable alternative exists, suggest it proactively.
This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Project-SOS is a multiplayer RTS game built with Unity 6 (6000.0.64f1) using Unity's Data-Oriented Tech Stack (DOTS):
- **Entity Component System (ECS)** via Unity Entities 1.4.3
- Unity Physics 1.4.3
    - Removed: Physics Shape, Physics Body, Physics Collider authoring components.
    - Use standard Unity Rigidbody & Colliders. They auto-convert to DOTS physics entities via the Baking System.
- **Netcode for Entities** 1.10.0 for multiplayer synchronization
- **Client-Server Architecture** with authoritative server

## Build & Development

**Opening the Project:**
- Open in Unity Hub with Unity 6000.0.64f1
- Solution file: `Project-SOS.sln` (contains 6 assemblies)

**Required Unity Settings:**
- Editor > Enter Play Mode Settings > Do not reload Domain or Scene
- Player > Run in Background (checked)

**Running Multiplayer:**
- AutoConnect Port: 7979 (defined in GameBootStrap.cs)
- Use Multiplayer Play Mode package for testing client/server locally

## Code Architecture

### Assembly Structure
```
Assets/Scripts/
├── Shared/          # Components, RPCs, systems used by both client & server
├── Client/          # Input handling, UI, visualization systems
├── Server/          # Server authority, game logic enforcement
└── Authoring/       # GameObject → Entity conversion (baking)
```

Each folder has a corresponding `.asmdef` file defining module dependencies.

### Detailed Folder Structure

```
Client/
├── Component/
│   ├── Catalog/
│   │   ├── StructurePrefabIndexMap.cs       # 건물 프리팹 인덱스 맵
│   │   └── UnitPrefabIndexMap.cs            # 유닛 프리팹 인덱스 맵
│   ├── Commands/
│   │   └── PendingBuildRequest.cs           # 대기 중인 건설 요청
│   ├── Singleton/
│   │   ├── NotificationState.cs             # 서버 알림 상태 싱글톤
│   │   ├── SelectedEntityInfoState.cs       # 현재 선택된 엔티티 정보 싱글톤
│   │   ├── UserSelectionInputState.cs       # 유저 선택 입력 상태 (Phase 기반)
│   │   └── UserState.cs                     # 유저 컨텍스트 (Command/BuildMenu/Construction/StructureActionMenu)
│   └── Structures/
│       └── StructurePreviewState.cs         # 건설 프리뷰 상태
├── Controller/
│   ├── StructurePreviewController.cs        # 건설 프리뷰 GameObject 컨트롤러
│   └── UI/
│       ├── Commands/
│       │   ├── CommandButton.cs             # 재사용 커맨드 버튼 컴포넌트
│       │   ├── SelectionBoxRenderer.cs      # 드래그 박스 렌더링
│       │   ├── StructureCommandUIController.cs  # 건물 명령 UI (통합)
│       │   └── UnitCommandUIController.cs   # 유닛 명령 UI (통합)
│       ├── Info/
│       │   ├── EntityInfoRenderer.cs        # 엔티티 정보 UI
│       │   └── UserResourceInfoRenderer.cs  # 유저 경제 UI 렌더러
│       └── ToastNotificationController.cs   # 토스트 알림 UI
├── Systems/
│   ├── Combat/
│   │   └── ClientDeathSystem.cs             # 클라이언트 사망 처리
│   ├── Commands/
│   │   ├── Construction/
│   │   │   ├── ConstructionMenuInputSystem.cs   # Q키 건설 메뉴 (BuildSelectionUtility 사용)
│   │   │   ├── PendingBuildExecuteSystem.cs     # 대기 건설 요청 실행
│   │   │   ├── StructurePlacementInputSystem.cs # 건설 배치 입력
│   │   │   └── StructurePreviewUpdateSystem.cs  # 프리뷰 업데이트
│   │   ├── FireProjectileClientRpcSystem.cs # 투사체 발사 RPC 처리 (클라이언트)
│   │   ├── Selection/
│   │   │   ├── EntitySelectionSystem.cs     # Selected 컴포넌트 토글
│   │   │   ├── SelectedEntityInfoUpdateSystem.cs  # 선택된 엔티티 정보 업데이트
│   │   │   ├── SelectionVisualizationSystem.cs
│   │   │   └── UserSelectionInputUpdateSystem.cs  # 마우스 입력 → Phase 업데이트
│   │   ├── StructureAction/
│   │   │   └── StructureCommandInputSystem.cs   # 건물 명령 입력 (생산/자폭)
│   │   └── UnitControl/
│   │       └── UnitCommandInputSystem.cs    # 유닛 명령 입력 (이동/공격)
│   ├── Initialize/
│   │   ├── CatalogIndexMapInitSystem.cs     # 카탈로그 인덱스 맵 초기화
│   │   ├── ClientBootstrapSystem.cs         # 클라이언트 초기화
│   │   └── GoInGameClientSystem.cs          # 게임 진입 클라이언트
│   ├── NotificationReceiveSystem.cs         # 서버 알림 RPC 수신
│   └── Rendering/
│       ├── CarriedResourceVisualizationSystem.cs  # 운반 자원 시각화
│       └── WorkerVisibilitySystem.cs        # 일꾼 가시성 시스템
├── UI/
│   └── Enemy/
│       ├── EnemyHpTextPresentationSystem.cs # 적 HP 텍스트 프레젠테이션
│       └── EnemyHpUIBridge.cs               # 적 HP UI 브릿지
└── Utilities/
    ├── BuildSelectionUtility.cs             # 건설 명령 공통 유틸리티
    └── ProduceSelectionUtility.cs           # 생산 명령 공통 유틸리티

Shared/
├── Buffers/
│   ├── EntityCatalog.cs                     # 엔티티 카탈로그 버퍼
│   ├── GridCell.cs                          # 그리드 셀 버퍼
│   ├── PathWaypoint.cs                      # 경로 웨이포인트 버퍼
│   └── StructurePrefabStore.cs              # 건물 프리팹 저장소
├── Components/
│   ├── Data/
│   │   ├── EnemyChaseDistance.cs            # 적 추적 거리
│   │   ├── ExplosionData.cs                 # 폭발 데이터
│   │   ├── GridOccupancyCleanup.cs
│   │   ├── GridPosition.cs
│   │   ├── NavMeshObstacleProxy.cs          # NavMesh 장애물 프록시
│   │   ├── ProductionInfo.cs
│   │   ├── ProductionQueue.cs
│   │   ├── ProjectileMove.cs                # 투사체 이동
│   │   ├── UserCurrency.cs
│   │   └── UserPopulation.cs
│   ├── Flags/
│   │   └── SelectedTag.cs                   # Selected (EnableableComponent)
│   ├── Inputs/
│   │   └── UnitCommand.cs                   # 유닛 명령 입력
│   ├── Movement/
│   │   ├── MovementGoal.cs                  # 이동 목표
│   │   ├── MovementSpeed.cs
│   │   ├── MovementWaypoints.cs             # 이동 웨이포인트
│   │   └── SeparationForce.cs               # 분리력 (충돌 회피)
│   ├── State/
│   │   ├── ConstructionState.cs
│   │   ├── EnemyState.cs                    # 적 상태
│   │   ├── GatheringTarget.cs               # 자원 채집 타겟
│   │   ├── ResourceNodeState.cs             # 자원 노드 상태
│   │   ├── StructureState.cs
│   │   ├── Target.cs
│   │   ├── UnitActionState.cs               # 유닛 액션 상태
│   │   ├── UnitIntentState.cs               # 유닛 의도 상태
│   │   ├── UnitState.cs                     # 유닛 상태 (Idle/Moving/Combat)
│   │   └── WorkerState.cs
│   ├── Stats/
│   │   ├── CombatStats.cs
│   │   ├── Defense.cs
│   │   ├── GatheringAbility.cs              # 자원 채집 능력
│   │   ├── Health.cs
│   │   ├── ObstacleRadius.cs                # 장애물 반경
│   │   ├── ProductionCost.cs
│   │   ├── ResourceNodeSetting.cs           # 자원 노드 설정
│   │   ├── StructureFootprint.cs
│   │   ├── VisionRange.cs
│   │   └── WorkRange.cs                     # 작업 사거리
│   └── Tags/
│       ├── IdentityTags.cs                  # UnitTag, StructureTag
│       ├── SelfDestructTag.cs               # 자폭 태그
│       ├── StructureTypeTags.cs             # WallTag, ProductionFacilityTag, ResourceCenterTag 등
│       ├── Team.cs                          # 팀 태그
│       ├── UnitTypeTags.cs                  # HeroTag, WorkerTag, BuilderTag, SwordsmanTag, TrooperTag, SniperTag 등
│       └── UserEconomyTag.cs                # 유저 경제 엔티티 태그
├── RPCs/
│   ├── BuildRequestRpc.cs                   # 건설 요청 RPC
│   ├── FireProjectileRpc.cs                 # 투사체 발사 RPC
│   ├── GatherRequestRpc.cs                  # 자원 채집 요청 RPC
│   ├── GoInGameRequestRpc.cs                # 게임 진입 요청 RPC
│   ├── NotificationRpc.cs                   # 서버 알림 RPC (자원 부족 등)
│   ├── ProduceUnitRequestRpc.cs             # 유닛 생산 요청 RPC
│   └── SelfDestructRequestRpc.cs            # 자폭 요청 RPC
├── Singletons/
│   ├── GhostIdMap.cs                        # Ghost ID 맵 싱글톤
│   ├── GridSettings.cs
│   └── Ref/
│       ├── EnemyPrefabRef.cs                # 적 프리팹 참조
│       ├── ProjectilePrefabRef.cs           # 투사체 프리팹 참조
│       ├── ResourceNodePrefabRef.cs         # 자원 노드 프리팹 참조
│       └── UserEconomyPrefabRef.cs          # 유저 경제 프리팹 참조
├── Systems/
│   ├── Combats/
│   │   └── ProjectileMoveSystem.cs          # 투사체 이동 시스템
│   ├── Commands/
│   │   └── CommandProcessingSystem.cs       # 명령 처리 시스템
│   ├── Enemy/
│   │   ├── EnemyMoveSystem.cs               # 적 이동 시스템
│   │   ├── EnemySpawnerSystem.cs            # 적 스폰 시스템
│   │   └── EnemyTargetSystem.cs             # 적 타겟 시스템
│   ├── Grid/
│   │   ├── GridOccupancyEventSystem.cs
│   │   └── ObstacleGridInitSystem.cs        # 장애물 그리드 초기화 시스템
│   ├── Movement/
│   │   ├── MovementArrivalSystem.cs         # 이동 도착 처리
│   │   ├── PredictedMovementSystem.cs       # 예측 기반 유닛 이동 시스템
│   │   └── UnitSeparationSystem.cs          # 유닛 분리 시스템 (충돌 회피)
│   └── Utils/
│       └── GhostIdLookupSystem.cs           # Ghost ID 조회 시스템
└── Utilities/
    ├── DamageUtility.cs                     # 데미지 계산 유틸리티
    └── GridUtility.cs                       # 그리드 유틸리티

Server/
├── Data/
│   └── BuildActionRequest.cs                # 건설 액션 요청 데이터
├── GoInGameServerSystem.cs
└── Systems/
    ├── Combat/
    │   ├── CombatDamageSystem.cs            # 전투 데미지 시스템 (서버)
    │   └── ServerDeathSystem.cs             # 서버 사망 처리
    ├── Commands/
    │   ├── Construction/
    │   │   └── HandleBuildRequestSystem.cs
    │   ├── Gathering/
    │   │   └── HandleGatherRequestSystem.cs # 자원 채집 요청 처리
    │   ├── Production/
    │   │   ├── HandleProduceUnitRequestSystem.cs    # 유닛 생산 요청 처리
    │   │   └── ProductionProgressSystem.cs          # 생산 진행 시스템
    │   └── Structure/
    │       ├── HandleSelfDestructRequestSystem.cs  # 자폭 요청 처리
    │       └── SelfDestructTimerSystem.cs          # 자폭 타이머
    ├── FireProjectileServerSystem.cs        # 투사체 발사 (서버)
    ├── Gathering/
    │   ├── ResourceNodeCleanupSystem.cs     # 자원 노드 정리 시스템
    │   └── WorkerGatheringSystem.cs         # 일꾼 자원 채집 시스템
    ├── Movement/
    │   ├── NavMeshObstacleCleanupSystem.cs  # NavMesh 장애물 정리
    │   ├── NavMeshObstacleSpawnSystem.cs    # NavMesh 장애물 생성
    │   ├── PathfindingSystem.cs             # Pathfinding 시스템
    │   └── PathFollowSystem.cs              # 경로 추적 시스템
    └── Physics/
        └── StructurePushOutSystem.cs        # 건물 충돌 밀어내기

Authoring/
├── CatalogAndRef/
│   ├── EnemyPrefabRefAuthoring.cs           # 적 프리팹 참조
│   ├── ProjectilePrefabRefAuthoring.cs      # 투사체 프리팹 참조
│   ├── ResourceNodePrefabRefAuthoring.cs    # 자원 노드 프리팹 참조
│   ├── StructureCatalogAuthoring.cs         # 건물 카탈로그
│   ├── UnitCatalogAuthoring.cs              # 유닛 카탈로그
│   └── UserEconomyPrefabRefAuthoring.cs     # 유저 경제 프리팹 참조 싱글톤
├── Combat/
│   └── ProjectileAuthoring.cs               # 투사체 오서링
├── Components/
│   └── SelectedAuthoring.cs                 # Selected 태그 오서링
├── Economy/
│   └── UserEconomyAuthoring.cs              # 유저 경제 오서링 (Ghost 프리팹용)
├── Entities/
│   ├── EnemyAuthoring.cs                    # 적 오서링
│   ├── ResourceNodeAuthoring.cs             # 자원 노드 오서링
│   ├── StructureAuthoring.cs                # 건물 오서링 (Wall/Barracks/Turret/ResourceCenter)
│   └── UnitAuthoring.cs                     # 유닛 오서링 (Hero/Worker/Soldier/Swordsman/Trooper/Sniper)
├── Movement/
│   └── UnitMovementAuthoring.cs             # 유닛 이동 오서링
└── Settings/
    └── GridSettingsAuthoring.cs

Root (Scripts)/
└── GameBootStrap.cs                         # 게임 진입점
```

### Key Patterns

**User State Machine** (`Client/Component/Singleton/UserState.cs`):
```csharp
public enum UserContext : byte {
    Command = 0,              // 기본 명령 상태 (유닛/건물 선택)
    BuildMenu = 1,            // 건설 메뉴 (빌더 유닛 Q 누른 상태)
    Construction = 2,         // 건물 배치 모드
    StructureActionMenu = 10, // 건물 명령 메뉴 (생산 시설 Q 누른 상태)
    Dead = 255,               // 사망/게임오버
}
```

**Selection System Flow** (Phase 기반 이벤트 처리):
```
[GhostInputSystemGroup]
UserSelectionInputUpdateSystem      → UserSelectionInputState.Phase 업데이트
EntitySelectionSystem                → Phase가 Pending*일 때만 Selected 토글
SelectedEntityInfoUpdateSystem       → SelectedEntityInfoState 싱글톤 계산
UnitCommandInputSystem               → 우클릭 명령 생성 (이동/공격)
```

**Network RPCs** (in `Shared/RPCs/`):
- `GoInGameRequestRpc` - Client join request
- `BuildRequestRpc` - Building placement request
- `FireProjectileRpc` - 투사체 발사 요청
- `GatherRequestRpc` - 자원 채집 요청
- `ProduceUnitRequestRpc` - 유닛 생산 요청
- `SelfDestructRequestRpc` - 건물 자폭 요청
- `NotificationRpc` - 서버→클라이언트 알림 (자원 부족 등)

**UI 통합 구조** (공통 패턴):

유닛 커맨드 UI (`UnitCommandUIController`):
```
unitCommandPanel
├── unitNameText                    # 유닛 이름
├── commandButtonPanel              # Command 상태 버튼들
│   ├── buildButton                 # 빌더 유닛만 표시 (Q → BuildMenu)
│   └── (향후: attackButton, stopButton 등)
└── buildMenuPanel                  # BuildMenu 상태
    └── buildButtons[4]             # 건설 가능 건물 (Q/W/E/R)
```

건물 커맨드 UI (`StructureCommandUIController`):
```
structureCommandPanel
├── structureNameText               # 건물 이름
├── commandButtonPanel              # Command 상태 버튼들
│   ├── produceButton               # 생산 시설만 표시 (Q → StructureActionMenu)
│   ├── selfDestructButton          # 벽만 표시 (R → 즉시 자폭)
│   └── (향후: rallyPointButton, attackTargetButton 등)
└── productionMenuPanel             # StructureActionMenu 상태
    └── produceButtons[3]           # 생산 가능 유닛 (Q/W/E)
```

**공통 유틸리티**:
- `BuildSelectionUtility`: 건설 명령 공통 로직 (UI + 키보드 입력 공유)
- `ProduceSelectionUtility`: 생산 명령 공통 로직 (UI + 키보드 입력 공유)
- `CommandButton`: 재사용 가능한 버튼 컴포넌트 (이름, 단축키, 비용, 해금 상태)

**키 바인딩**:

유닛 선택 시 (Command 상태):
- `Q`: 빌더 유닛 → BuildMenu 진입

건설 메뉴 (BuildMenu 상태):
- `Q/W/E/R`: 건물 선택 → Construction 모드
- `ESC`: 메뉴 닫기

건물 선택 시 (Command 상태):
- `Q`: 생산 시설 → StructureActionMenu 진입
- `R`: 벽 → 즉시 자폭

생산 메뉴 (StructureActionMenu 상태):
- `Q/W/E`: 유닛 생산
- `ESC`: 메뉴 닫기

### Key Files

- `GameBootStrap.cs` - Entry point, extends ClientServerBootstrap, sets target framerate (60fps)
- `Shared/Buffers/EntityCatalog.cs` - 유닛/건물 프리팹 카탈로그 버퍼
- `Shared/Singletons/Ref/ProjectilePrefabRef.cs` - 투사체 프리팹 참조

## Prefabs

```
Assets/Prefabs/
├── Enemy/
│   └── Enemy.prefab                 # 적 프리팹
├── Ground.prefab                    # 지면 프리팹
├── Resources/
│   └── OreVein.prefab               # 광석 자원 프리팹
├── Shoot/
│   └── Projectile.prefab            # 투사체 프리팹
├── Structures/
│   ├── Barracks.prefab              # 병영 건물
│   ├── ResourceCenter.prefab        # 자원 센터
│   └── Wall.prefab                  # 벽 건물
├── UI/
│   └── EnemyHPText3D.prefab         # 적 HP 3D 텍스트 UI
├── Units/
│   ├── Hero.prefab                  # 영웅 유닛
│   ├── Sniper.prefab                # 저격수 유닛 (장거리)
│   ├── Swordsman.prefab             # 검사 유닛 (근접)
│   ├── Trooper.prefab               # 보병 유닛 (중거리)
│   └── Worker.prefab                # 일꾼 유닛 (건설 가능)
└── UserResources/
    └── UserResources.prefab         # 유저 자원 Ghost 프리팹
```

## Scenes

```
Assets/Scenes/
├── InGame.unity              # 메인 게임플레이 씬
└── InGame/
    └── EntitiesSubScene.unity  # DOTS 엔티티 서브씬
```

---

## Game Design

### Game State (Wave System)

Wave 단계마다 적대 유닛의 수, 종류가 증가하며 난이도 상승

| Wave | 설명 |
|------|------|
| Wave0 | 게임 진입 상태. 임의의 수의 적(벽 통과 불가)이 벽에 갇혀있음. 벽은 일정 시간(~30초) 후 제거됨 |
| Wave1 | 일정 시간(~3분) 경과 또는 초기 적 일정 수 제거 시 다음 웨이브로 전환. 벽 사이로 들어오는 적 스폰 시작 |
| Wave2+ | 벽 사이로 들어오는 적, 벽을 뛰어넘는 적, 나는 적 등 다양한 유형 스폰 |
| END | 모든 웨이브 완료 시 게임 종료, 유저 승리 |

### Commander (User) State

**Command (기본 상태)**
- 유닛 선택: 좌클릭 드래그(다수), 단일 클릭(단일)
- 유닛 이동: 우클릭 좌표로 이동 명령

**Construction**
- 건설 기능이 있는 유닛 선택 → Q키 또는 UI 건설 버튼
- 건물 종류 선택(단축키 또는 UI) → 건설 모드 진입

**Dead (사망)**
- 싱글 플레이: 패배 팝업 → 게임 종료
- 멀티 플레이:
  - 타 플레이어 생존 시: 화면 중앙 깃발로 표시, 관전 모드(유닛/건물 정보 확인 가능)
  - 전원 사망 시: 패배 팝업 → 게임 종료

### Unit State

| 상태 | 설명 |
|------|------|
| Idle | 기본 상태 |
| Moving | 유닛 선택 후 우클릭 → 해당 좌표로 이동 |
| Combat | 유닛 선택 후 적 유닛/건물 우클릭 → 전투 상태 |
| Dead | HP 0 → Entity 삭제 |

---

## Development Guidelines (개발 지침)

### 기본 원칙

1. **Burst Compile 필수**: 모든 로직은 `[BurstCompile]` 적용을 원칙으로 한다.
   - 예외: 사용자 입력 로직
   - 예외: managed 타입 (class 등)

2. **Job System 활용**: 단순 데이터 조작이 아닌 연산 로직은 메인 스레드(`OnUpdate`)가 아닌 `partial struct ... : IJobEntity`로 구현하여 멀티스레드를 활용한다.

3. **네이밍 규칙**: 코드베이스 전체에서 일관된 용어 사용을 위한 네이밍 규칙을 준수한다.
   - **"Player" 금지**: 유저를 지칭할 때 "Player" 대신 **"User"**를 사용한다.
     - 예: `UserResources`, `UserState`, `UserResourcesTag`
     - 이유: 멀티플레이어 환경에서 "Player"는 Unity Player(실행 인스턴스)와 혼동될 수 있음

## Unity DOTS: Data Access & Validity Rules

1. **Safe Lookup Patterns**
    * **Main Thread (EntityManager):** `EntityManager` natively lacks `TryGetComponent`.
        * Use the pattern: `if (manager.HasComponent<T>(e)) { var data = manager.GetComponentData<T>(e); }`.
    * **Jobs/Random Access (Lookup):** Use `ComponentLookup<T>.TryGetComponent(entity, out T data)`.
    * **Singletons:** Use `SystemAPI.TryGetSingleton<T>(out T result)`, TryGetSingletonEntity.

2. **Minimize Permissions**
    * Prefer **`RefRO<T>`** over `RefRW<T>` (and `[ReadOnly]` for Lookups) whenever mutation is unnecessary.
    * This maximizes Job Scheduler parallel execution efficiency.

3. **Tag Components over Flags**
    * Avoid checking `bool` fields inside loops for state management.
    * Use **Tag Components** (empty structs) and filter at the Query level (`.WithAll<T>`, `.WithNone<T>`).
    * This improves Archetype chunk utilization and skips irrelevant entities entirely.

4. **Null & Validity Checks**
    * **Never** use `== null` for ECS value types (Structs).
    * **Entities:** Compare against `Entity.Null` or use `EntityManager.Exists(e)`.
    * **Native Collections / BlobAssets:** Check the `.IsCreated` property.

### 엔티티 처리

1. **ECB 사용 의무화**: 엔티티 생성/파괴/컴포넌트 변경은 메인 스레드에서 즉시 수행하지 않고, **`EndSimulationEntityCommandBufferSystem`** 등을 통해 얻은 `EntityCommandBuffer`에 기록하여 처리한다.
   - 직접 `new EntityCommandBuffer()` 금지
   - SystemState에서 ECB 시스템을 통해 획득

### 싱글톤 초기화

1. **중앙 집중 초기화**: `UserState` 등 싱글톤 데이터는 한 곳(예: `ClientBootstrapSystem`)에 모아 초기화한다. 분산된 초기화는 의존성 문제와 초기화 순서 버그를 유발할 수 있다.
