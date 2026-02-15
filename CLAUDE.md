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
0. **Docs 폴더 참조 (필수)**: 작업을 시작하기 전에 반드시 `Docs/` 폴더의 관련 문서를 먼저 읽고 현재 시스템 구조와 동작 방식을 파악한다. 문서를 참조하지 않고 구현에 착수하지 않는다.
1. **Verify Existing Patterns**: Before implementing new logic (especially singletons), check if similar patterns or components already exist in the codebase to avoid duplication.
2. **Validate User Instructions**: Critically evaluate whether the user's instruction aligns with actual facts in the codebase. If the user's assumption is incorrect or outdated, inform them of the discrepancy.
3. **Assess Efficiency**: Determine if the user's proposed approach is the most efficient solution. If a more performant or maintainable alternative exists, suggest it proactively.

---

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

**상세 파일 목록**: [Docs/코드베이스 구조.md](Docs/코드베이스%20구조.md)

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
    NavMeshObstacleSpawnSystem → PathfindingSystem (Collect→Compute×8→Apply) → PathFollowSystem
    PredictedMovementSystem (SpatialMaps.MovementMap 사용)
    MovementArrivalSystem → BuildArrivalSystem (도착 시 건설)

[6. 전투] FixedStepSimulationSystemGroup (Server)
    CombatDamageSystem → MeleeAttackSystem → RangedAttackSystem → DamageApplySystem
    ※ 모든 데미지는 DamageEvent 버퍼를 통해 DamageApplySystem에서 일괄 적용 + 적 킬 카운트

[7. 정리] SimulationSystemGroup (Server)
    HeroDeathDetectionSystem → ServerDeathSystem → NavMeshObstacleCleanupSystem, TechStateRecalculateSystem

[7.5. 네트워크 최적화] SimulationSystemGroup (Server)
    UpdateConnectionPositionSystem (CameraPositionRpc 수신) → GhostRelevancySystem (뷰포트 AABB × 1.3 밖 적 Ghost 전송 차단)
    MinimapDataBroadcastSystem → MinimapBatchRpc 분산 전송 (적 위치 RPC)

[7.6. 미니맵] SimulationSystemGroup (Client)
    MinimapDataReceiveSystem → MinimapDataState (Double buffer 스왑)
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
- `PathfindingSystem`: ISystem(unmanaged), NavMeshQuery×8 병렬 IJob, `NavMeshPathUtils.FindStraightPath` (Funnel 알고리즘)
- `SpatialMapBuildSystem`: Persistent 맵 + Job 기반 Clear → dependency chain으로 동기화 (CompleteDependency 불필요)
- `GhostRelevancySystem`: UpdateAfter `UpdateConnectionPositionSystem` (Ghost Relevancy AABB 필터링, ViewHalfExtent × 1.3/1.15)

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

## Development Guidelines

### 기본 원칙
1. **Burst Compile 필수**: 모든 로직은 `[BurstCompile]` 적용 (예외: 입력 로직, managed 타입)
2. **Job System 활용**: 연산 로직은 `IJobEntity`로 구현하여 멀티스레드 활용
3. **네이밍**: "Player" 대신 **"User"** 사용 (Unity Player와 혼동 방지). 변수명은 의미를 알 수 있도록 작성하며, 축약하지 않는다 (예: `var c` ✗ → `var teamColor` ✓). 단, DOTS 관용 약어(`ecb`, `em`, `job` 등)는 허용.
4. **테스트**: EditMode(순수 함수) / PlayMode(ECS 시스템) 테스트 작성

### Burst 제약사항
1. **`[BurstCompile]` static 메서드**: struct(`float3`, `Entity` 등)를 값으로 전달/반환하면 BC1064 에러 발생 (external function 제약). struct 파라미터/반환이 있는 메서드는 `[BurstCompile]` 제거하고 `[MethodImpl(AggressiveInlining)]`만 사용. primitive(`float`, `int`, `bool`)만 다루는 메서드만 개별 `[BurstCompile]` 적용 가능. 클래스 레벨 `[BurstCompile]`은 유지.
2. **`bool` 필드 blittable**: Burst 컴파일되는 struct에 `bool` 필드가 있고 `ref`로 전달되면 `[MarshalAs(UnmanagedType.U1)]` 필수. `[GhostField]`와 별개 목적.

### Unity DOTS Rules
1. **Safe Lookup**: `ComponentLookup<T>.TryGetComponent()`, `SystemAPI.TryGetSingleton<T>()`
2. **Minimize Permissions**: `RefRO<T>` 선호, `[ReadOnly]` Lookup 사용
3. **Tag Components**: bool 필드 대신 Tag 컴포넌트 + Query 필터링
4. **Null Checks**: `Entity.Null` 비교, `EntityManager.Exists()`, `.IsCreated` 프로퍼티
5. **ECB 사용**: 엔티티 생성/파괴/컴포넌트 변경은 `EndSimulationEntityCommandBufferSystem` 통해 처리
6. **싱글톤 초기화**: `ClientBootstrapSystem` 등 한 곳에 집중

### Combat System Rules
1. **DamageEvent 버퍼**: Health 직접 수정 금지 → DamageEvent 버퍼 사용 (위 코드 예제 참조)
2. **CompleteDependency 최소화**: 같은 SystemGroup 내에서는 `UpdateAfter`로 순서 지정
3. **AggroTarget**: 유닛/적 공통 타겟 추적 컴포넌트
4. **원거리 공격**: `RangedUnitTag`/`RangedEnemyTag` → 필중 + 시각 투사체(VisualOnlyTag) 생성

---

## Documentation

### Docs 폴더 구조
```
Docs/
├── 코드베이스 구조.md           # 전체 파일/폴더 구조, 어셈블리 목록
├── 시스템 그룹 및 의존성.md      # SystemGroup 정의, 시스템 간 의존성
├── 엔티티 선택 시스템.md         # 선택 Phase, SelectionRing, 관련 컴포넌트
├── 엔티티 이동 시스템(navmesh).md # NavMesh, PathfindingSystem, MovementGoal
├── 엔티티 전투.md               # 공격 시스템, DamageEvent, AggroTarget
├── 건설 시스템.md               # 건물 배치, BuildRequestRpc, 그리드 점유
├── 자원 채집 시스템.md           # 자원 수집, CarriedResource, 반납 로직
├── 유저 자원, 인구수.md          # UserEconomy, Population 시스템
├── Project-SOS 상태 시스템 설계.md # UserContext 상태 머신, UI 상태
├── 팀 색상 시스템.md               # TeamColorSystem, TeamColorPalette, 팀별 틴트
└── 성능 분석/
    ├── 대량 엔티티 이동 끊김 분석.md  # PathfindingSystem 병렬화, Ghost 대역폭 최적화
    └── Ghost Relevancy 및 미니맵 RPC 전환.md # Ghost Relevancy, 미니맵 RPC 시스템
```

### 문서 업데이트 규칙 (필수)
**구현 완료 후 반드시 관련 문서를 업데이트해야 한다.**

| 변경 유형 | 업데이트 대상 문서 |
|----------|-------------------|
| 새 시스템 추가 | `시스템 그룹 및 의존성.md`, `코드베이스 구조.md` |
| 새 컴포넌트 추가 | `코드베이스 구조.md`, 관련 기능 문서 |
| 선택 로직 변경 | `엔티티 선택 시스템.md` |
| 이동 로직 변경 | `엔티티 이동 시스템(navmesh).md` |
| 전투 로직 변경 | `엔티티 전투.md` |
| 건설 로직 변경 | `건설 시스템.md` |
| 자원/채집 변경 | `자원 채집 시스템.md`, `유저 자원, 인구수.md` |
| UI 상태 변경 | `Project-SOS 상태 시스템 설계.md` |
| 새 RPC 추가 | `코드베이스 구조.md` (RPCs 섹션), 관련 기능 문서 |

**문서 작성 원칙**:
1. **코드와 동기화**: 문서 내용이 실제 코드와 일치해야 함
2. **간결함 유지**: 핵심 로직과 데이터 흐름 중심으로 작성
3. **예제 포함**: 복잡한 패턴은 코드 예제로 설명
4. **CLAUDE.md 동기화**: 주요 패턴/플로우 변경 시 CLAUDE.md도 함께 업데이트

### 커밋 메시지 작성 가이드

사용자가 커밋 메시지 작성을 요청하면 다음 형식을 따른다:

```
<제목: 작업 내용 요약 (한 줄)>

<세부 작업 내용>
- 변경된 파일/시스템 목록
- 수정 의도 및 해결한 문제
- 주요 변경 사항
```

**작성 원칙**:
1. **제목**: 무엇을 했는지 명확하게 요약 (예: "자원 반납 시 ResourceCenter 소유권 검증 추가")
2. **본문**: 제목과 두 줄 띄우고 세부 내용 작성
3. **의도 명시**: 단순 변경 사항 나열이 아닌, 왜 이 변경이 필요했는지 드러나도록 작성
4. **간결함**: 불필요한 설명 없이 핵심만 기술
5. **Co-Authored-By 금지**: 커밋 메시지에 `Co-Authored-By` 트레일러를 절대 추가하지 않는다
