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
- **접속 방식**: 룸 서버 경유 수동 연결 (AutoConnect 비활성화, `RoomClient` + `NetcodeConnectionUtil` 사용)

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
| `Client/Network/` | 룸 서버 클라이언트 + Netcode 수동 연결 | `RoomClient.cs`, `NetcodeConnectionUtil.cs` |
| `Server/Network/` | 룸 서버 토큰 검증 + 슬롯 관리 | `RoomTokenValidator.cs`, `SlotNotifyClient.cs` |
| `Shared/Network/` | 프로토콜 프레이밍 + protoc 생성 코드 | `ProtobufFraming.cs`, `Room.cs` |
| `Server/Systems/Combat/` | 전투 로직 | `*AttackSystem.cs`, `*DamageSystem.cs` |
| `Shared/Components/Tags/` | 태그 컴포넌트 | `*Tag.cs` |
| `Shared/RPCs/` | 네트워크 RPC | `*Rpc.cs`, `*RequestRpc.cs` |
| `Shared/Components/Animation/` | VAT 애니메이션 상태/클립 | `VATAnimation*.cs` |
| `Shared/Components/Sound/` | 사운드 타입 정의 | `SoundType.cs` |
| `Client/Component/Animation/` | VAT 셰이더 파라미터 | `VATAnimParam.cs`, `VATAnimTarget.cs` |
| `Client/Component/Sound/` | 사운드 이벤트 버퍼 | `SoundEvent.cs` |
| `Client/Systems/Animation/` | VAT 재생 + 전투 기울임 | `VATAnimation*System.cs`, `CombatTiltSystem.cs` |
| `Client/Systems/Sound/` | 사운드 이벤트 발생 | `SoundEventEmitSystem.cs` |
| `Client/Controller/Sound/` | 사운드 매니저 | `SoundManager.cs` |
| `Server/Systems/Animation/` | VAT 상태 갱신 (서버) | `VATAnimationStateUpdateSystem.cs` |
| `Authoring/Animation/` | VAT 애니메이션 오서링 | `VATAnimationAuthoring.cs` |
| `Editor/VATBaker/` | VAT 베이킹 에디터 툴 | `VATBaker*.cs` |
| `Shaders/` | VAT 셰이더 | `VATAnimation.shader` |
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

[6.5. 애니메이션 상태] SimulationSystemGroup (Server, UpdateAfter FixedStepSimulationSystemGroup)
    VATAnimationStateUpdateSystem (UnitActionState/EnemyState → VATAnimationState.CurrentClipIndex 갱신, Ghost 동기화)

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

[9.7. 애니메이션+사운드] SimulationSystemGroup (Client)
    VATAnimationInitSystem (새 메시 엔티티에 VATAnimParam/VATAnimTarget/PreviousClipIndex 부착)
    → VATAnimationPlaybackSystem (VATAnimParam 계산, IJobEntity [BurstCompile])
    → SoundEventEmitSystem (상태 변화 + AttackSpeed 타이머 → SoundEvent 버퍼, 공격 반복/사망/스폰)
    → CombatTiltSystem (Attacking 상태 전방 기울임, VAT 유무 무관, IJobEntity [BurstCompile])
    → TeamColorSystem (기존)
    → SoundManager (MonoBehaviour, 매 프레임 SoundEvent 버퍼 소비 + AudioSource 풀 재생)

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

## 접속 흐름 (Room Server Integration)

```
[1. 로비] RoomClient (MonoBehaviour, TCP :8080)
    앱 시작 → RoomClient가 룸 서버에 TCP 연결
    → 로비/대기실 UI → GameStart 메시지 수신
    → AuthToken + SessionId + ServerAddress 획득

[2. Netcode 연결] NetcodeConnectionUtil (static)
    GameStart 수신 → NetcodeConnectionUtil.Connect(serverAddress, port)
    → Netcode ClientWorld 수동 연결 (AutoConnect 비활성화)
    → RoomAuthState 싱글톤에 AuthToken + SessionId 저장

[3. 게임 진입] GoInGameClientSystem (Client)
    GoInGameRequestRpc 전송 (AuthToken 포함)

[4. 토큰 검증] TokenValidationSystem (Server, UpdateBefore: GoInGameServerSystem)
    GoInGameRequestRpc 수신 → RoomTokenValidator로 룸 서버 :8081 TCP 검증
    → 검증 성공: TokenValidatedTag + RoomSessionInfo(SessionId, UserId) 부착
    → 검증 실패: 연결 거부

[5. Hero 생성] GoInGameServerSystem (Server, WithAll<TokenValidatedTag> 필터)
    TokenValidatedTag가 있는 Connection만 Hero 생성 처리

[6. 슬롯 관리] SlotNotifySystem (Server)
    연결 끊김 감지 → SlotReleased 메시지 → 룸 서버 :8081 TCP
    30초 간격 하트비트 전송
```

**프로토콜**: Protobuf (Sos.Room 네임스페이스) + 4byte LE length-prefix 프레이밍 (`ProtobufFraming`)

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
| 유닛 (Hero, Worker 등) | `MovementAuthoring` + `UnitMovementAuthoring` + `UnitAuthoring` (+ `VATAnimationAuthoring` for Hero) |
| 적 (Enemy) | `MovementAuthoring` + `EnemyAuthoring` (+ `VATAnimationAuthoring` for EnemySmall/EnemyFlying) |
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

### 6. VAT Animation Pattern
GPU Animation (Vertex Animation Texture) 방식으로 수천 유닛을 동시 애니메이팅. Animator/SkinnedMeshRenderer 없이 MeshRenderer + 커스텀 셰이더만 사용.
- **베이킹**: 에디터 툴(`VATBakerWindow`)로 스켈레탈 애니메이션 → Position Texture(RGBAHalf) + Static Mesh(UV2 버텍스 인덱스) + VATClipDataAsset 생성
- **서버**: `VATAnimationStateUpdateSystem`이 UnitActionState/EnemyState → `VATAnimationState.CurrentClipIndex` 갱신 (Ghost 동기화)
- **클라이언트**: `VATAnimationPlaybackSystem`이 `VATAnimParam`(MaterialProperty float4) 계산 → 셰이더가 텍스처 룩업으로 버텍스 변형
- **사운드**: `SoundEventEmitSystem`이 상태 변화 감지 + `CombatStats.AttackSpeed` 타이머로 공격 반복 재생 + 자기 유닛 스폰 감지(`GhostOwnerIsLocal` 필터) → `SoundEvent` 버퍼 → `SoundManager`(MonoBehaviour)가 AudioSource 풀로 재생 (타입별 볼륨 조절). Ghost 재생성 시 이미 Attacking인 엔티티는 타이머를 공격 간격으로 초기화하여 즉시 발동 방지.
- **전투 기울임**: `CombatTiltSystem`이 Attacking 상태에서 Rotation pitch 조작 (VAT 유무와 무관, 전체 유닛/적 대상)
- **대상**: VAT 적용(Hero, EnemySmall, EnemyFlying) + VAT 미적용(Worker/Striker/Tank/Archer/EnemyBig은 기존 정적 메시 유지, 기울임+사운드만)

### 7. Network RPCs
`MoveRequestRpc`, `AttackRequestRpc`, `BuildRequestRpc`, `BuildMoveRequestRpc`, `GatherRequestRpc`, `ReturnResourceRequestRpc`, `ProduceUnitRequestRpc`, `SelfDestructRequestRpc`, `CameraPositionRpc`, `NotificationRpc`, `HeroDeathRpc`, `GameOverRpc`, `MinimapBatchRpc`

### 8. Room Server Token Validation Pattern
룸 서버를 통한 인증 흐름. Netcode 연결 전에 룸 서버에서 토큰을 발급받고, 서버가 이를 검증한다.
- **클라이언트**: `RoomClient`(MonoBehaviour)가 룸 서버 TCP :8080 연결 → `GameStart` 메시지로 토큰/세션/서버주소 수신 → `NetcodeConnectionUtil`로 Netcode 수동 연결 → `GoInGameRequestRpc`에 토큰 포함
- **서버**: `TokenValidationSystem`(managed SystemBase)이 `RoomTokenValidator`로 룸 서버 :8081 TCP 검증 → 성공 시 `TokenValidatedTag` + `RoomSessionInfo` 부착 → `GoInGameServerSystem`은 `WithAll<TokenValidatedTag>` 필터로 검증된 클라이언트만 Hero 생성
- **슬롯 관리**: `SlotNotifySystem`이 연결 끊김 감지 시 `SlotReleased` 전송 + 30초 하트비트
- **프로토콜**: Protobuf (`Sos.Room`) + 4byte LE length-prefix 프레이밍 (`ProtobufFraming`)

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
