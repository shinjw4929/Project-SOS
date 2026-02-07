# Project-SOS 상태 시스템 설계

## 개요

게임 상태는 **클라이언트 전용**과 **서버 동기화**로 구분됩니다.

| 구분 | 상태 | 파일 경로 | 설명 |
|------|------|-----------|------|
| Client | UserState | `Client/Component/Singleton/UserState.cs` | 사용자 조작 맥락 |
| Client | UserSelectionInputState | `Client/Component/Singleton/UserSelectionInputState.cs` | 마우스 입력/선택 Phase |
| Client | SelectedEntityInfoState | `Client/Component/Singleton/SelectedEntityInfoState.cs` | 선택된 엔티티 정보 |
| Client | StructurePreviewState | `Client/Component/Structures/StructurePreviewState.cs` | 건설 프리뷰 상태 |
| Client | CameraState | `Client/Component/Singleton/CameraState.cs` | 카메라 모드 |
| Client | NotificationState | `Client/Component/Singleton/NotificationState.cs` | 알림 표시 상태 |
| Client | GameOverEvents | `Client/Events/GameOverEvents.cs` | 게임오버 이벤트 (정적 클래스) |
| Server | GamePhaseState | `Shared/Singletons/GamePhaseState.cs` | Wave 진행 상태 (싱글톤) |
| Server | UserAliveState | `Shared/Components/Data/UserAliveState.cs` | 유저 생존 상태 (Connection 엔티티) |
| Server | UserTechState | `Shared/Components/Data/UserTechState.cs` | 테크 해금 상태 |
| Server | UnitIntentState | `Shared/Components/State/UnitIntentState.cs` | 유닛 행동 의도 |
| Server | UnitActionState | `Shared/Components/State/UnitActionState.cs` | 유닛 실제 동작 |
| Server | AggroTarget | `Shared/Components/State/AggroTarget.cs` | 공격/추적 대상 (유닛/적 공통) |
| Server | AggroLock | `Shared/Components/State/AggroLock.cs` | 어그로 고정 (피격 시) |
| Server | EnemyState | `Shared/Components/State/EnemyState.cs` | 적 AI 상태 |
| Server | StructureState | `Shared/Components/State/StructureState.cs` | 건물 상태 |
| Server | WorkerState | `Shared/Components/State/WorkerState.cs` | 일꾼 자원 채집 상태 |
| Server | GatheringTarget | `Shared/Components/State/GatheringTarget.cs` | 채집 대상/반납 지점 |
| Server | ResourceNodeState | `Shared/Components/State/ResourceNodeState.cs` | 자원 노드 점유 상태 |

---

## 1. 클라이언트 상태

### 1.1 UserState

사용자의 현재 조작 모드를 나타내는 최상위 상태입니다.

**UserContext**

| 상태 | 값 | 설명 |
|------|:--:|------|
| Command | 0 | 기본 상태. 유닛/건물 선택 및 명령 |
| BuildMenu | 1 | 건설 메뉴 (Worker 선택 후 Q키) |
| Construction | 2 | 건물 배치 모드 |
| StructureActionMenu | 10 | 건물 명령 메뉴 (건물 선택 후 Q키) |
| Dead | 255 | 사망/게임오버 (조작 불능) |

**상태 전이**

```
                      ┌─────────┐
                      │ Command │ ◄──────────────────┐
                      └────┬────┘                    │
                           │                         │
           ┌───────────────┼───────────────┐         │
           │               │               │         │
           ▼               ▼               │         │
    ┌───────────┐   ┌──────────────────┐   │    [ESC]│
    │ BuildMenu │   │StructureActionMenu│  │         │
    └─────┬─────┘   └────────┬─────────┘   │         │
          │                  │             │         │
     [건물선택]          [명령실행]         │         │
          │                  │             │         │
          ▼                  └─────────────┘         │
    ┌──────────────┐                                 │
    │ Construction │ ───────[배치완료/ESC]───────────┘
    └──────────────┘
                      │
              [Hero 사망 시]
                      ▼
               ┌──────────┐
               │   Dead   │ (탈출 불가)
               └──────────┘
```

---

### 1.2 UserSelectionInputState

마우스 클릭/드래그를 Phase 기반 상태 머신으로 처리합니다.

**SelectionPhase**

| Phase | 값 | 설명 |
|-------|:--:|------|
| Idle | 0 | 대기 |
| Pressing | 1 | 마우스 버튼 누름 (클릭/드래그 구분 전) |
| Dragging | 2 | 드래그 중 (선택 박스 렌더링) |
| PendingClick | 3 | 단일 클릭 처리 대기 |
| PendingBox | 4 | 박스 선택 처리 대기 |

**상태 전이**

```
             [마우스 다운]         [이동 거리 > 5px]
     Idle ──────────────► Pressing ──────────────► Dragging
                               │                        │
                        [마우스 업]               [마우스 업]
                               │                        │
                               ▼                        ▼
                         PendingClick             PendingBox
                               │                        │
                      [EntitySelectionSystem 처리]      │
                               │                        │
                               └───────► Idle ◄─────────┘
```

**데이터 필드**

| 필드 | 타입 | 용도 |
|------|------|------|
| Phase | SelectionPhase | 현재 선택 단계 |
| StartScreenPos | float2 | 드래그 시작 화면 좌표 |
| CurrentScreenPos | float2 | 현재 마우스 화면 좌표 |

---

### 1.3 SelectedEntityInfoState

UI 표시 및 명령 가능 여부 판단용 싱글톤입니다.

**SelectionCategory**

| Category | 값 | 설명 |
|----------|:--:|------|
| None | 0 | 선택 없음 |
| Units | 1 | 유닛만 선택됨 |
| Structure | 2 | 건물만 선택됨 |
| Uncontrolled | 3 | 적/자원 등 컨트롤 불가 |

**데이터 필드**

| 필드 | 타입 | 용도 |
|------|------|------|
| PrimaryEntity | Entity | 대표 엔티티 (UI 정보 표시용) |
| SelectedCount | int | 선택된 엔티티 수 |
| Category | SelectionCategory | 선택 카테고리 |
| IsOwnedSelection | bool | 내 소유 여부 |

---

### 1.4 StructurePreviewState

Construction 모드에서 건물 배치 상태를 시각화합니다.

**PlacementStatus**

| Status | 값 | 설명 |
|--------|:--:|------|
| Invalid | 0 | 건설 불가 (빨간색) |
| ValidInRange | 1 | 건설 가능 + 사거리 내 (초록색) |
| ValidOutOfRange | 2 | 건설 가능 + 사거리 밖 (노란색) |

**데이터 필드**

| 필드 | 타입 | 용도 |
|------|------|------|
| SelectedPrefab | Entity | 선택된 건물 프리팹 |
| SelectedPrefabIndex | int | 서버 전송용 인덱스 |
| GridPosition | int2 | 마우스가 가리키는 그리드 좌표 |
| IsValidPlacement | bool | 배치 가능 여부 (하위 호환용) |
| Status | PlacementStatus | 3단계 배치 상태 |
| DistanceToBuilder | float | 빌더와의 거리 |

---

### 1.5 CameraState

카메라 동작 모드를 관리합니다.

**CameraMode**

| Mode | 값 | 설명 |
|------|:--:|------|
| EdgePan | 0 | 화면 가장자리 이동 (기본값) |
| HeroFollow | 1 | 히어로 추적 모드 |

**데이터 필드**

| 필드 | 타입 | 용도 |
|------|------|------|
| CurrentMode | CameraMode | 현재 카메라 모드 |
| TargetEntity | Entity | HeroFollow 모드 추적 대상 |

---

### 1.6 NotificationState

UI 알림 표시를 위한 싱글톤입니다.

**데이터 필드**

| 필드 | 타입 | 용도 |
|------|------|------|
| PendingNotification | NotificationType | 표시할 알림 타입 (None이면 표시 안 함) |

---

### 1.7 GameOverEvents

게임오버 관련 이벤트를 관리하는 정적 클래스입니다. (ECS 싱글톤 폴링 대신 이벤트 기반)

**이벤트**

| 이벤트 | 설명 |
|--------|------|
| OnHeroDeath | 내 히어로 사망 시 발생 |
| OnGameOver | 게임오버 시 발생 (모든 유저 사망) |

**관련 시스템**
- `HeroDeathDetectionSystem` (서버): Hero 사망 감지 → HeroDeathRpc/GameOverRpc 전송
- `GameOverReceiveSystem` (클라이언트): RPC 수신 → UserState를 Dead로 변경 → 이벤트 발생
- `GameOverPanelController` (MonoBehaviour): 이벤트 구독하여 게임오버 UI 표시

**게임오버 흐름**
```
[Server] Hero.Health <= 0
         ↓
  HeroDeathDetectionSystem
  ├─ UserAliveState.IsAlive = false
  └─ HeroDeathRpc 전송 (해당 유저에게)
         ↓
  (모든 유저 사망 시)
  └─ GameOverRpc 브로드캐스트
         ↓
[Client] GameOverReceiveSystem
  ├─ UserState = Dead
  └─ GameOverEvents.RaiseHeroDeath() / RaiseGameOver()
         ↓
  GameOverPanelController.ShowGameOverPanel()
```

---

## 2. 서버 동기화 상태

### 2.1 GamePhaseState

게임 진행 상태를 추적하는 **서버 싱글톤**입니다.

**WavePhase**

| Phase | 값 | 설명 |
|-------|:--:|------|
| Wave0 | 0 | 초기 상태: EnemyBig만 스폰 |
| Wave1 | 1 | EnemySmall 추가 스폰 시작 |
| Wave2 | 2 | EnemyFlying 추가 스폰 시작 |

**데이터 필드**

| 필드 | 타입 | 용도 |
|------|------|------|
| CurrentWave | WavePhase | 현재 Wave 단계 |
| ElapsedTime | float | 게임 시작 후 경과 시간 (초) |
| TotalKillCount | int | 총 적 처치 수 |
| Wave0SpawnedCount | int | Wave0에서 스폰한 적 수 |
| SpawnTimer | float | 마지막 주기적 스폰 이후 경과 시간 |

---

### 2.2 UserAliveState

유저 생존 상태를 추적하는 컴포넌트입니다. **Connection 엔티티에 부착**되어 Hero 파괴 후에도 상태 추적이 가능합니다.

**데이터 필드**

| 필드 | 타입 | 용도 |
|------|------|------|
| IsAlive | bool | 생존 여부 |
| HeroEntity | Entity | 연결된 Hero 엔티티 |

**사용 시스템**
- `GoInGameServerSystem`: 게임 입장 시 UserAliveState 초기화 (IsAlive = true)
- `HeroDeathDetectionSystem`: Hero 사망 시 IsAlive = false로 업데이트

---

### 2.3 UserTechState

유저별 테크 해금 상태를 추적합니다. UserEconomy 엔티티에 함께 저장됩니다.

**데이터 필드**

| 필드 | 타입 | 용도 |
|------|------|------|
| HasResourceCenter | bool | ResourceCenter 보유 여부 (Barracks 해금 조건) |

**사용 시스템**
- `TechStateRecalculateSystem`: 건물 생성/파괴 시 테크 상태 재계산

---

### 2.4 UnitIntentState

서버가 판단한 "유닛이 무엇을 하려는가"에 대한 상태입니다.

**Intent**

| 상태 | 값 | 설명 |
|------|:--:|------|
| Idle | 0 | 정지 (자동 타겟팅 활성화) |
| Move | 1 | 이동 명령 (적 무시) |
| Hold | 2 | 위치 사수 `(미구현)` |
| Patrol | 3 | 순찰 `(미구현)` |
| Build | 4 | 건설 이동 + 건설 중 |
| Gather | 5 | 채집 이동 + 채집 중 |
| Attack | 6 | 공격 이동 + 공격 중 |
| AttackMove | 7 | 이동 중 적 발견 시 교전 |

**데이터 필드**

| 필드 | 타입 | 용도 |
|------|------|------|
| State | Intent | 현재 의도 |
| TargetEntity | Entity | 대상 엔티티 |
| TargetLastKnownPos | float3 | 타겟의 마지막 위치 |

---

### 2.5 UnitActionState

애니메이션 및 물리 시스템이 참조하는 현재 동작 상태입니다.

**Action**

| 상태 | 값 | 설명 |
|------|:--:|------|
| Idle | 0 | 가만히 서 있음 |
| Moving | 1 | 이동 중 |
| Working | 2 | 채집/건설/수리 중 |
| Attacking | 3 | 공격 모션 중 |
| Disabled | 200 | 기절/마비 |
| Dying | 254 | 사망 연출 중 |
| Dead | 255 | 사망 완료 |

> **Note**: Stop, Holding 상태는 삭제됨. Intent + Action 조합으로 표현 (예: Intent.Hold + Action.Idle)

**Intent → Action 매핑**

| Intent | 가능한 Action |
|--------|---------------|
| Idle | Idle |
| Move | Moving → Idle |
| Attack | Moving (추격) / Attacking (사거리 내) |
| Build | Moving → Working |
| Gather | Moving → Working |
| Hold | Idle / Attacking |

---

### 2.6 AggroTarget

유닛/적 공통 공격 대상 추적 컴포넌트입니다.

**데이터 필드**

| 필드 | 타입 | 용도 |
|------|------|------|
| TargetEntity | Entity | 현재 공격/추적 대상 |
| LastTargetPosition | float3 | 마지막 확인 위치 |

**사용 시스템**
- `UnifiedTargetingSystem`: 적/유닛 자동 타겟팅
- `MeleeAttackSystem`: 근접 공격 판정
- `RangedAttackSystem`: 원거리 공격 판정

---

### 2.7 AggroLock

피격 시 어그로 대상을 일정 시간 고정하는 컴포넌트입니다.

**데이터 필드**

| 필드 | 타입 | 용도 |
|------|------|------|
| LockedTarget | Entity | 고정된 어그로 대상 |
| RemainingLockTime | float | 남은 고정 시간 (초) |
| LockDuration | float | 고정 지속 시간 (설정값) |

**동작 방식**

```
피격 발생 (DamageEvent.Attacker)
       ↓
AggroReactionSystem
├─ LockedTarget = Attacker
├─ RemainingLockTime = LockDuration
└─ AggroTarget.TargetEntity = Attacker

       ↓ 매 프레임
UnifiedTargetingSystem
├─ RemainingLockTime -= deltaTime
├─ RemainingLockTime > 0 → 타겟 변경 불가
└─ RemainingLockTime <= 0 → 정상 타겟팅
```

**사용 시스템**
- `AggroReactionSystem`: 피격 시 어그로 고정 설정
- `UnifiedTargetingSystem`: 고정 시간 동안 타겟 변경 방지

---

### 2.8 EnemyState

적 유닛의 AI 행동 상태입니다.

**EnemyContext**

| 상태 | 값 | 설명 |
|------|:--:|------|
| Idle | 0 | 대기 |
| Wandering | 1 | 배회 중 |
| Attacking | 2 | 공격 중 |
| Chasing | 3 | 추격 중 |
| Disabled | 20 | 기절/마비 |
| Dying | 254 | 사망 연출 중 |
| Dead | 255 | 사망 |

**상태 전이**

```
      ┌──────┐       [타겟 발견]    ┌─────────┐
      │ Idle │ ──────────────────► │ Chasing │
      └──┬───┘                     └────┬────┘
         │                              │
         │ [타겟 없음]          [사거리 도달]
         │                              │
         ▼                              ▼
    ┌───────────┐                ┌───────────┐
    │ Wandering │                │ Attacking │
    └───────────┘                └───────────┘
         │                              │
         └────────[타겟 발견]───────────┘
                        │
                        ▼
                   Chasing
```

**Wandering 상태 상세**

배회 로직은 `UnifiedTargetingSystem`의 `EnemyTargetJob`/`EnemyWanderOnlyJob`에서 처리합니다.

| 항목 | 내용 |
|------|------|
| 트리거 | 타겟 없음 (`AggroTarget.TargetEntity == Entity.Null`) |
| 목적지 범위 | GridSettings 기반 맵 범위 (가장자리 5유닛 여유) |
| 랜덤 시드 | `entity.Index ^ (FrameCount * 0x9E3779B9) ^ (ElapsedTime * 1000)` |
| 경로 계산 | `MovementGoal.IsPathDirty = true` → PathfindingSystem |
| 재배회 조건 | 목적지 도착 (`!waypointsEnabled`) 또는 타겟 발견 |

---

### 2.9 StructureState

건물의 현재 상태입니다.

**StructureContext**

| 상태 | 값 | 설명 |
|------|:--:|------|
| Idle | 0 | 대기 |
| Constructing | 1 | 건설 중 `(현재 즉시 건설)` |
| Active | 2 | 작업 중 (생산/공격) |
| Destroyed | 255 | 파괴됨 |

---

### 2.10 WorkerState

Worker 유닛의 자원 채집 상태입니다.

**ResourceType**

| Type | 값 | 설명 |
|------|:--:|------|
| None | 0 | 자원 없음 |
| Cheese | 1 | 치즈 자원 |

**GatherPhase**

| Phase | 값 | 설명 |
|-------|:--:|------|
| None | 0 | 채집 중 아님 |
| MovingToNode | 1 | 자원 노드로 이동 중 |
| Gathering | 2 | 자원 채집 중 |
| MovingToReturn | 3 | 반납 지점으로 이동 중 |
| Unloading | 4 | 자원 하차 중 |
| WaitingForNode | 5 | 노드 점유 대기 중 |

**채집 사이클**

```
  ┌──────────────┐    ┌────────────┐    ┌─────────────────┐
  │ MovingToNode │ ─► │ Gathering  │ ─► │ MovingToReturn  │
  └──────────────┘    └────────────┘    └────────┬────────┘
          ▲                                      │
          │                                      ▼
          │                               ┌───────────┐
          └────────────────────────────── │ Unloading │
                                          └───────────┘
```

**데이터 필드**

| 필드 | 타입 | 용도 |
|------|------|------|
| CarriedAmount | int | 현재 자원 보유량 |
| CarriedType | ResourceType | 자원 종류 |
| GatheringProgress | float | 채집 진행도 (0~1) |
| Phase | GatherPhase | 채집 사이클 단계 |

---

### 2.11 GatheringTarget

Worker의 채집 대상 및 반납 지점 정보입니다.

**데이터 필드**

| 필드 | 타입 | 용도 |
|------|------|------|
| ResourceNodeEntity | Entity | 목표 자원 노드 |
| ReturnPointEntity | Entity | 자원 반납 지점 (ResourceCenter) |
| AutoReturn | bool | 반납 후 자동 복귀 여부 |
| LastGatheredNodeEntity | Entity | 마지막 채굴 노드 |

---

### 2.12 ResourceNodeState

자원 노드의 실시간 상태입니다.

**데이터 필드**

| 필드 | 타입 | 용도 |
|------|------|------|
| OccupyingWorker | Entity | 현재 점유 중인 워커 |

---

## 3. 전체 흐름

### 3.1 사용자 입력 → 명령 처리

```
┌─────────────────────────────────────────────────────────────────────┐
│                          CLIENT                                     │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  [마우스 입력]                                                      │
│        │                                                            │
│        ▼                                                            │
│  UserSelectionInputUpdateSystem (Phase 업데이트)                    │
│        │                                                            │
│        ▼                                                            │
│  EntitySelectionSystem (Selected 토글)                              │
│        │                                                            │
│        ▼                                                            │
│  SelectedEntityInfoUpdateSystem (선택 정보 계산)                    │
│        │                                                            │
│        ├──────────────────────────────────────────┐                 │
│        ▼                                          ▼                 │
│  [유닛 선택됨]                              [건물 선택됨]           │
│  UserState = Command                        UserState = Command     │
│        │                                          │                 │
│        │ [Q키]            [우클릭]            [Q키]│                 │
│        ▼                    │                     ▼                 │
│  UserState                  │             UserState                 │
│  = BuildMenu                │             = StructureActionMenu     │
│        │                    │                     │                 │
│  [건물 선택]                │              [명령 실행]              │
│        ▼                    │                     │                 │
│  UserState                  │                     │                 │
│  = Construction             │                     │                 │
│        │                    │                     │                 │
└────────┼────────────────────┼─────────────────────┼─────────────────┘
         │                    │                     │
         ▼                    ▼                     ▼
     ┌────────────────────────────────────────────────────────┐
     │                      RPC 전송                          │
     ├────────────────────────────────────────────────────────┤
     │  BuildRequestRpc        MoveRequestRpc    ProduceUnitRpc│
     │  BuildMoveRequestRpc    AttackRequestRpc               │
     │  GatherRequestRpc       ReturnResourceRequestRpc       │
     └────────────────────────────────────────────────────────┘
         │                    │                     │
         ▼                    ▼                     ▼
┌─────────────────────────────────────────────────────────────────────┐
│                          SERVER                                     │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  HandleBuildRequestSystem   HandleMoveRequestSystem                 │
│  HandleBuildMoveRequestSystem HandleAttackRequestSystem             │
│  HandleGatherRequestSystem  HandleReturnResourceRequestSystem       │
│                                                                     │
│        │                  │                         │               │
│        ▼                  ▼                         ▼               │
│  UnitIntentState       UnitIntentState         ProductionQueue      │
│  = Build               = Move / Attack / Gather    업데이트         │
│        │                  │                                         │
│        ▼                  ▼                                         │
│  UnitActionState       UnitActionState                              │
│  = Moving → Working    = Moving → Attacking                         │
│                                                                     │
│  AggroTarget 갱신 (UnifiedTargetingSystem)                          │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

### 3.2 Worker 자원 채집 흐름

```
┌────────────────────────────────────────────────────────────────┐
│  [GatherRequestRpc 수신]                                       │
│             │                                                  │
│             ▼                                                  │
│  UnitIntentState.State = Gather                                │
│  GatheringTarget.ResourceNodeEntity = 자원 노드                │
│  GatheringTarget.ReturnPointEntity = ResourceCenter            │
│  WorkerState.Phase = MovingToNode                              │
│             │                                                  │
│             ▼                                                  │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │            WorkerGatheringSystem 루프                   │   │
│  ├─────────────────────────────────────────────────────────┤   │
│  │                                                         │   │
│  │  MovingToNode ──[도착]──► Gathering                     │   │
│  │       ▲                       │                         │   │
│  │       │                   [완료]                        │   │
│  │       │                       ▼                         │   │
│  │  Unloading ◄───[도착]─── MovingToReturn                 │   │
│  │       │                                                 │   │
│  │   [완료]                                                │   │
│  │       ▼                                                 │   │
│  │  CarriedAmount = 0                                      │   │
│  │  UserCurrency += 반납량                                 │   │
│  │       │                                                 │   │
│  │       └───────────► MovingToNode (AutoReturn=true)      │   │
│  │                                                         │   │
│  └─────────────────────────────────────────────────────────┘   │
│                                                                │
└────────────────────────────────────────────────────────────────┘
```

### 3.3 전투 흐름

```
┌────────────────────────────────────────────────────────────────┐
│  [AttackRequestRpc 수신 또는 자동 타겟팅]                      │
│             │                                                  │
│             ▼                                                  │
│  HandleAttackRequestSystem                                     │
│  → UnitIntentState.State = Attack                              │
│  → UnitIntentState.TargetEntity = 적 엔티티                    │
│             │                                                  │
│             ▼                                                  │
│  UnifiedTargetingSystem                                        │
│  → AggroTarget.TargetEntity 갱신                               │
│  → AggroTarget.LastTargetPosition 갱신                         │
│             │                                                  │
│             ▼                                                  │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │  [사거리 밖]              [사거리 내]                   │   │
│  │       │                        │                        │   │
│  │       ▼                        ▼                        │   │
│  │  UnitActionState          UnitActionState               │   │
│  │  = Moving (추격)          = Attacking                   │   │
│  │       │                        │                        │   │
│  │       │                        ▼                        │   │
│  │       │              MeleeAttackSystem (근접)           │   │
│  │       │              RangedAttackSystem (원거리)        │   │
│  │       │                        │                        │   │
│  │       │                        ▼                        │   │
│  │       │                  DamageEvent 버퍼               │   │
│  │       │                        │                        │   │
│  │       │                        ▼                        │   │
│  │       │                  DamageApplySystem              │   │
│  │       │                  → Health 감소                  │   │
│  └───────┴────────────────────────┴────────────────────────┘   │
│                                                                │
└────────────────────────────────────────────────────────────────┘
```

### 3.4 Hero 사망 및 게임오버 흐름

```
┌────────────────────────────────────────────────────────────────┐
│                          SERVER                                 │
├────────────────────────────────────────────────────────────────┤
│                                                                │
│  [Hero.Health <= 0 감지]                                       │
│             │                                                  │
│             ▼                                                  │
│  HeroDeathDetectionSystem (ServerDeathSystem 이전 실행)        │
│  ├─ NetworkId → Connection 엔티티 매핑                         │
│  ├─ UserAliveState.IsAlive = false                             │
│  └─ HeroDeathRpc 전송 (해당 유저에게)                          │
│             │                                                  │
│             ▼                                                  │
│  [모든 UserAliveState.IsAlive == false?]                       │
│       │ Yes                                                    │
│       ▼                                                        │
│  GameOverRpc 브로드캐스트 (모든 클라이언트)                    │
│                                                                │
└────────────────────────────────────────────────────────────────┘
         │
         ▼
┌────────────────────────────────────────────────────────────────┐
│                          CLIENT                                 │
├────────────────────────────────────────────────────────────────┤
│                                                                │
│  GameOverReceiveSystem                                         │
│  ├─ HeroDeathRpc 수신                                          │
│  │   ├─ UserState.CurrentState = Dead                          │
│  │   └─ GameOverEvents.RaiseHeroDeath()                        │
│  │                                                             │
│  └─ GameOverRpc 수신                                           │
│      └─ GameOverEvents.RaiseGameOver()                         │
│             │                                                  │
│             ▼                                                  │
│  GameOverPanelController (MonoBehaviour)                       │
│  ├─ OnGameOver 이벤트 구독                                     │
│  └─ 게임오버 패널 표시                                         │
│                                                                │
└────────────────────────────────────────────────────────────────┘
```
