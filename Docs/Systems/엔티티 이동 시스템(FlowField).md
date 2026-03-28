# 엔티티 이동 시스템 (Flow Field)

# 전체 흐름

```
[Client] 우클릭 → MoveRequestRpc (IsAttackMove 플래그 포함)
    ↓
[Server] HandleMoveRequestSystem → MovementGoal 설정, Intent.Move 또는 Intent.AttackMove
    ↓
[SpatialPartitioningGroup]
SpatialMapBuildSystem → MovementMap 빌드 (셀 크기: 3.0f)
    ↓
[SimulationSystemGroup]
GridObstacleResponseSystem → 건물 건설 시 IsPathBlocked 마킹 + 유닛 밀어내기 + 캐시 무효화
    ↓
GridObstacleCleanupSystem → 건물 파괴 시 IsPathBlocked 해제 + 캐시 무효화
    ↓
FlowFieldSystem → BFS 기반 Flow Field 계산 (LRU 캐시 128슬롯, 8 IJob 병렬, 프레임당 BFS 상한)
    ↓                Wandering 적은 BFS 바이패스 (직선 이동)
    ↓
    ↓
FlowFieldSteeringSystem → Flow Field 방향 조회 → MovementWaypoints.Current/Next 공급
    ↓
PredictedMovementSystem → LocalTransform 직접 이동 (Steering 회피, 벽 투과 방지)
    ↓
MovementArrivalSystem → 도착 판정 → 이동 정지 + Intent.Idle + Action.Idle 전환
```

# 핵심 설계 포인트

1. 서버 권위 모델: 모든 이동 계산은 서버에서 수행, 클라이언트는 Ghost 보간으로 시각화
2. 2계층 구조: MovementGoal(명령) → MovementWaypoints(실행). FlowField가 매 프레임 방향을 주입하므로 별도 웨이포인트 버퍼 불필요
3. Kinematic 이동: PhysicsVelocity가 아닌 LocalTransform.Position 직접 수정
4. 대역폭 최적화: MovementWaypoints/MovementGoal 필드는 서버 전용 ([GhostField] 없음), PhysicsVelocity도 동기화 안 함 (Quantization=0). Ghost enabled 상태와 LocalTransform만 동기화
5. 공간 분할 충돌 회피: SpatialMaps.MovementMap(셀 크기 3.0f)을 사용한 Entity 기반 Separation
6. AttackMove 지원: 이동 중 적 자동 감지 (Intent.AttackMove 상태)
7. 단일 그리드: 1.0m 셀, 배치(IsOccupied)와 경로탐색(IsPathBlocked) 분리
8. 벽 반투과: 배치 4×4, 경로탐색 2×2 중앙 → Small 유닛 통과, Large 유닛 차단

## 클라이언트 시스템 (Client/Systems/)

| 파일 | 그룹 | 역할 |
| --- | --- | --- |
| UnitCommandInputSystem.cs | GhostInputSystemGroup | 우클릭 → Physics Raycast → MoveRequestRpc 생성 및 전송 (선택된 모든 유닛 대상) |

## 서버 시스템 (Server/Systems/)

### 공간 분할 시스템 (Server/Systems/Spatial/)

파일: SpatialMapBuildSystem.cs
그룹: SpatialPartitioningGroup (OrderFirst=true)
역할: 공간 분할 맵 빌드
- MovementMap (셀 크기: 3.0f): 이동 충돌 회피용 (대형 유닛 AABB 등록)
- Persistent 맵을 매 프레임 Job 기반 Clear 후 재빌드 (CompleteDependency 불필요)
- 결과를 SpatialMaps 싱글톤에 저장

---

### 명령 처리 시스템 (Server/Systems/Commands/Movement/)

파일: HandleMoveRequestSystem.cs
그룹: SimulationSystemGroup
역할: MoveRequestRpc 수신 → 소유권 검증 → MovementGoal.Destination 설정
- IsPathDirty=true
- UnitIntentState = rpc.IsAttackMove ? Intent.AttackMove : Intent.Move
- UnitActionState = Action.Moving (걷기 애니메이션 트리거)
- AggroTarget 초기화 (공격 대상 제거)
- MovementWaypoints 활성화 (SetComponentEnabled)
- **군집 이동 (Group Formation)**: 동일 프레임+소유자+목적지(1m이내) 유닛을 그룹화 → FormationUtility로 격자 오프셋 적용 → 유닛별 개별 도착 좌표

---

### 장애물 시스템 (Server/Systems/Movement/)

파일: GridObstacleResponseSystem.cs
그룹: SimulationSystemGroup (UpdateBefore: FlowFieldSystem)
역할: 건물 건설 시 경로탐색 차단 + 유닛 밀어내기
- NeedsNavMeshObstacle 태그 감지 → IsPathBlocked 마킹 (MarkPathBlocked, 경로탐색 풋프린트 중앙)
- GridObstacleCleanup ICleanupComponentData 부착
- FlowFieldCacheData.IsGridStale=true 설정 (캐시 전체 무효화 트리거)
- 건물 내부 엔티티 자동 밀어내기: Width×CellSize/Length×CellSize + ObstacleRadius 바깥으로
- 8m 반경 내 이동 중인 유닛/적 경로 무효화 (IsPathDirty=true)
- NeedsNavMeshObstacle 비활성화
---
파일: GridObstacleCleanupSystem.cs
그룹: SimulationSystemGroup (UpdateAfter: ServerDeathSystem, UpdateBefore: FlowFieldSystem)
역할: 건물 파괴 시 경로탐색 차단 해제 + 캐시 무효화 + Dormant 적 깨우기
- GridObstacleCleanup(ICleanupComponentData) + WithNone<StructureTag, ResourceNodeTag>로 파괴된 건물 감지
- UnmarkPathBlocked로 IsPathBlocked 해제
- FlowFieldCacheData.IsGridStale=true 설정
- EnemyTag: 12m 반경 내 Dormant 적 즉시 `EnemyContext.Idle`로 전환 + IsPathPartial 적 경로 무효화
- UnitTag: 12m 반경 내 IsPathPartial 유닛 경로 무효화
- GridObstacleCleanup 컴포넌트 제거

---

### 이동 시스템 (Server/Systems/Movement/)

파일: FlowFieldSystem.cs
그룹: SimulationSystemGroup (UpdateAfter: GridObstacleResponseSystem)
역할: BFS 기반 Flow Field 계산 + LRU 캐시 관리
- **4단계 파이프라인**: Phase0(Passability) → Collect(메인) → Compute(8 IJob 병렬) → Apply(메인)
  - Phase 0: IsGridStale 시에만 passability 맵 재생성 (Small/Large 각각)
  - Collect: IsPathDirty 유닛 수집 (WithNone<FlyingTag>), 목적지 셀 추출, 캐시 히트/미스 분류
  - Compute: 캐시 미스 목적지에 대해 FlowFieldComputeJob 8개 병렬 BFS (Small 완료 → Large 순차)
  - Apply: FlowFieldRef 할당, MovementWaypoints 활성화, IsPathPartial 자동 판정 (`IsPathPartial = (IsDestAdjusted == 1)`, 매 프레임 클리어/설정)
- **Flying 유닛**: 별도 처리 (직선 이동, FlowField 스킵)
- **LRU 캐시**: 32 필드 × 2풀(Small/Large), Flat NativeArray + NativeHashMap, 그리드 변경 시 전체 무효화
- **Persistent 메모리**: 워커 8세트 (BfsQueue, Visited, CostMap) + FlowFieldCacheData 싱글톤
---
파일: FlowFieldSteeringSystem.cs
그룹: SimulationSystemGroup (UpdateAfter: FlowFieldSystem, UpdateBefore: PredictedMovementSystem)
역할: Flow Field 방향 조회 → MovementWaypoints 주입 + BuildApproachRadius 조기 정지
- 매 프레임 이동 중 유닛(MovementWaypoints enabled, WithNone<FlyingTag>) 순회
- BuildApproachRadius 보유 시: AABB 표면 거리² < Value²이면 웨이포인트 생성 중단 (건설 조기 정지)
- FlowFieldRef.Key로 캐시에서 Flow Field 조회, CellPadding으로 Small/Large 분기
- 현재 셀 방향 → 다음 셀 좌표 변환 → CellCenterToWorld
- 목적지 셀 판정: **좌표 비교** (currentCell == destCell), Current = Destination, HasNext = false
- 중간 셀: 2단계 look-ahead → HasNext = true, Next = look-ahead 셀 중심
- 캐시 미스 시: IsPathDirty=true (lazy re-pathing)
- FlowFieldRef.Key == -1 스킵
---
파일: PredictedMovementSystem.cs
그룹: SimulationSystemGroup (UpdateAfter: FlowFieldSteeringSystem)
역할: 핵심 이동 시스템
- Kinematic 방식: LocalTransform.Position 직접 수정
- 가속/감속 적용 (MovementDynamics)
- **Steering 기반 회피 (TimeSlice)**: SpatialMaps.MovementMap 사용 (셀 크기 3.0f). 이동 방향만 조정, 위치 직접 변경 없음 (밀림 없음). entityIndex 기반 결정론적 좌/우 분산
  - **TimeSlice**: `SteeringSliceDivisor`(기본 4) 프레임 주기로 회피 계산 분산. `entity.Index % Divisor == FrameCount % Divisor`인 프레임에만 이웃 탐색 실행, 나머지는 `CachedAvoidanceDir` 캐시 방향 재사용. 첫 프레임(Strength < 0.001f)은 즉시 계산
  - 적-유닛 간 회피: 항상 활성화
  - 유닛-유닛 간 회피: 한쪽이라도 작업 중(Gather/Build)이면 면제 (적 제외)
  - skipMovement=true (공격 중/정지) 시: Steering도 skip → 정지 유닛 완전 고정
- 벽 충돌: 이동 전 벽 검증 + 축별 분리 시도 + ResolveWallCollision (축 독립 검사, 벽 미끄러짐)
- 벽 관통 보정: `ClampToWall` — 이동 후 유닛 AABB vs IsPathBlocked 셀 겹침 검사, 최소 침투 축 방향으로 밀어냄 (5회 반복, 코너 대응)
---
파일: MovementArrivalSystem.cs
그룹: SimulationSystemGroup (UpdateAfter: PredictedMovementSystem)
역할: 도착 판정 및 상태 전환
- 1차 도착 조건: 거리 < ArrivalRadius && !HasNext
- 2차 도착 조건: 거리 < ArrivalRadius*2 && !HasNext && 거의 정지 상태 (저속 감지)
- 적(EnemyTag): MovementWaypoints 비활성화, 속도 0
- 유닛(UnitTag): 추가로 Intent.Move → Intent.Idle 전환 (자동 타겟팅 활성화) + UnitActionState → Action.Idle (Idle 애니메이션 복귀)

## Authoring (Authoring/Movement/)

| 파일 | 베이킹 컴포넌트 |
| --- | --- |
| MovementAuthoring.cs | MovementDynamics, MovementGoal, MovementWaypoints(비활성화), GridPathfindingSize, FlowFieldRef(Key=-1), CachedAvoidanceDir, Kinematic Mass (Rigidbody 없을 때만) |
| UnitMovementAuthoring.cs | UnitIntentState, UnitActionState, UnitCommand 버퍼. RequireComponent(MovementAuthoring) |

### MovementAuthoring 인스펙터 설정

| 필드 | 기본값 | 설명 |
| --- | --- | --- |
| MaxSpeed | 10.0 | 최대 이동 속도 (m/s) |
| Acceleration | 180.0 | 가속도 (m/s²) |
| Deceleration | 240.0 | 감속도 (m/s²) |
| RotationSpeed | 12.0 | 회전 속도 (rad/s) |
| PathfindingSize | 0 | 0=Small (좁은 통로 통과), 1=Large (벽 사이 갭 차단) |

> ArrivalRadius 필드는 제거됨. Baker에서 0으로 고정, MovementArrivalSystem에서 ObstacleRadius+0.1f fallback 적용.

## 유틸리티

### Shared/Utilities/

| 파일 | 역할 |
| --- | --- |
| FlowFieldCore.cs | BFS 기반 Flow Field 계산 (8방향, 코너 차단, 직교 우선 탐색). 방향 인코딩 0-7 + 255=None. `[BurstCompile]` struct, IJob 내부 호출 |
| GridUtility.cs | 그리드 좌표 변환, CellCenterToWorld, IsPassable, IsPassableForSize, BuildPassabilityMap, MarkPathBlocked/UnmarkPathBlocked. **BuildPassabilityMap**: CellPadding=0(Small)은 `IsPathBlocked` 그대로 복사, CellPadding=1(Large)은 3×3 범위 모두 passable해야 통과 가능 (Minkowski 합 확장) |
| MovementMath.cs | 감속 거리 계산(CalculateSlowingDistance), 목표 속도 계산(CalculateTargetSpeed), 가속/감속 적용(CalculateNewSpeed) |
| SpatialHashUtility.cs | 공간 분할 해시 계산 (중앙 집중화) |

### SpatialHashUtility 상수

| 상수 | 값 | 용도 |
| --- | --- | --- |
| TargetingCellSize | 10.0f | 타겟팅용 셀 크기 (적→아군, 유닛→적) |
| MovementCellSize | 3.0f | 이동/충돌 회피용 셀 크기 |
| CapacityMultiplier | 1.5f | 해시 충돌 방지 여유 계수 |

## 컴포넌트 (Shared/Components/Movement/)

| 파일 | 역할 |
| --- | --- |
| MovementGoal.cs | 최종 목적지(Destination), 경로 재계산 플래그(IsPathDirty), Partial 경로 플래그(IsPathPartial), 목적지 설정 시간(DestinationSetTime), 마지막 위치 체크(LastPositionCheck, LastPositionCheckTime), Dormant 깨어남 시간(DormantWakeTime). `[MarshalAs(UnmanagedType.U1)]` 적용 (IsPathDirty, IsPathPartial) |
| MovementWaypoints.cs | 현재 이동 목표(Current), 다음 지점(Next), HasNext, ArrivalRadius. IEnableableComponent로 이동 중/정지 상태 토글 |
| MovementDynamics.cs | 유닛 이동 파라미터: MaxSpeed, Acceleration, Deceleration, RotationSpeed |
| GridPathfindingSize.cs | 유닛 경로탐색 크기. CellPadding=0: Small, CellPadding=1: Large |
| FlowFieldRef.cs | Flow Field 캐시 조회 키 (destinationKey = destCell.y * gridSizeX + destCell.x). Key=-1: 미할당 |

### Ghost 동기화 필드

| 컴포넌트 | 동기화 | 비동기화 필드 (서버 전용) |
| --- | --- | --- |
| MovementGoal | enabled 상태만 | Destination, IsPathDirty, IsPathPartial, DestinationSetTime, LastPositionCheck, LastPositionCheckTime, DormantWakeTime |
| MovementWaypoints | enabled 상태만 | Current, Next, HasNext, ArrivalRadius |
| PhysicsVelocity | 없음 (Quantization=0) | Linear, Angular (PhysicsVelocityGhostOverride) |

**대역폭 최적화 근거**: 이동 시스템(FlowFieldSystem, FlowFieldSteeringSystem, PredictedMovementSystem)은 전부 서버 전용. 클라이언트는 Ghost 보간된 LocalTransform만으로 시각화하므로 이동 관련 필드 동기화 불필요.

## 싱글톤 (Shared/Singletons/)

| 파일 | 역할 |
| --- | --- |
| FlowFieldCacheData.cs | Flow Field 캐시 싱글톤. Small/Large FieldPool + KeyToPoolIndex(NativeHashMap) + LastUsedFrame(LRU) + PassabilityMap. IsGridStale 플래그로 전체 무효화 |
| GridSettings.cs | 그리드 설정. CellSize(1.0f), GridOrigin, GridSize(200×200), BuildSnapCells(1) |
| SpatialMaps.cs | 공간 분할 맵 싱글톤. TargetingMap(10.0f) + MovementMap(3.0f) 저장, IsValid 프로퍼티 |

## 시스템 실행 순서

```
[SpatialPartitioningGroup] ─────────────────────────────
SpatialMapBuildSystem (OrderFirst)
    → TargetingMap 빌드 (셀 크기: 10.0f)
    → MovementMap 빌드 (셀 크기: 3.0f)
    → SpatialMaps 싱글톤에 저장

[SimulationSystemGroup] ─────────────────────────────────
HandleMoveRequestSystem
    → MoveRequestRpc 수신
    → MovementGoal 설정, Intent 설정, MovementWaypoints 활성화
    ↓
GridObstacleResponseSystem (UpdateBefore: FlowFieldSystem)
    → 건물 IsPathBlocked 마킹 + 유닛 밀어내기 + IsGridStale=true
    ↓
GridObstacleCleanupSystem (UpdateAfter: ServerDeathSystem, UpdateBefore: FlowFieldSystem)
    → 건물 파괴 시 IsPathBlocked 해제 + IsGridStale=true
    ↓
FlowFieldSystem (UpdateAfter: GridObstacleResponseSystem)
    → Phase 0: IsGridStale → passability 맵 재생성 + 캐시 무효화
    → Phase 1: IsPathDirty=true 수집, 목적지 셀 추출, 캐시 히트/미스 분류
    → Phase 2: FlowFieldComputeJob × 8 병렬 BFS (Small → Large 순차)
    → Phase 3: FlowFieldRef 할당, MovementWaypoints 활성화, Partial Path 판정
    ↓
FlowFieldSteeringSystem (UpdateAfter: FlowFieldSystem, UpdateBefore: PredictedMovementSystem)
    → BuildApproachRadius 기반 조기 정지 (AABB 표면 거리² < workRange²)
    → Flow Field 방향 조회 → MovementWaypoints.Current/Next 주입
    → 2단계 look-ahead, 캐시 미스 시 lazy re-pathing
    ↓
PredictedMovementSystem (UpdateAfter: FlowFieldSteeringSystem)
    → LocalTransform.Position 직접 수정
    → SpatialMaps.MovementMap 기반 Separation
    → Grid 기반 벽 충돌 (축 독립 검사 + AABB 겹침 보정)
    → Separation 진동 감지 (확장 반경 내 밀려남 → 정지)
    ↓
MovementArrivalSystem (UpdateAfter: PredictedMovementSystem)
    → 1차 도착 판정 (반경 이내)
    → 2차 도착 판정 (확장 반경 + 방향 체크)
    → MovementWaypoints 비활성화
    → Intent.Move → Intent.Idle 전환

```

## 충돌 회피 로직

### PredictedMovementSystem 상세

```csharp
// 이동 스킵 조건 (Separation은 항상 실행)
bool isPathPending = goal.IsPathDirty;  // 경로 미계산 시 (0,0,0) 이동 방지
bool skipMovement = isAttacking || isWaypointsDisabled || isPathPending;

// 충돌 회피 조건
bool shouldCollide = iAmEnemy || isEnemy || (!iAmWorking && !isWorking);

// 적-유닛: 항상 충돌 회피
// 유닛-유닛: 한쪽이라도 작업 중(Gather/Build)이면 면제
// 적-적: 항상 충돌 회피
```

### 공격 중 Separation 유지

MeleeAttackSystem 등에서 ECB로 MovementWaypoints를 비활성화하면, 기본 쿼리로는 해당 엔티티가 제외되어 Separation이 미적용된다. 이를 해결하기 위해:

1. **쿼리**: `EntityQueryOptions.IgnoreComponentEnabledState`로 비활성화 엔티티 포함
2. **파라미터**: `EnabledRefRW<MovementWaypoints>`로 런타임에 활성화 상태 확인
3. **로직**: `skipMovement = isAttacking || isWaypointsDisabled || isPathPending` — 이동만 스킵, Separation은 유지

이 패턴은 `UnifiedTargetingSystem.EnemyTargetJob`에서도 동일하게 사용 중.

### Separation Force 계산

```csharp
// 비선형 force: 가까울수록 기하급수적으로 강해짐
float overlapRatio = overlap / combinedRadius; // 0~1
float forceMag = overlap * (1.0f + overlapRatio * 3.0f);

```

### 벽 충돌 처리 (Grid 기반)

1. **ResolveWallCollision (축 독립 검사)**: X축/Z축 이동을 각각 독립 검사하여 IsPathBlocked 셀과 겹치면 해당 축 속도를 제거. 유닛은 속력 보존 (벽을 따라 미끄러질 때 감속하지 않음), 적은 벡터 삭제.
2. **ClampToWall (안전망, 반복)**: 이동 후 유닛 AABB와 IsPathBlocked 셀의 겹침을 검사하여 최소 침투 축 방향으로 밀어냄. **최대 3회 반복**하여 코너(두 벽 교차) 관통 방지. 조기 종료 조건: 겹침 미검출.
3. **IsOverlappingBlockedCell**: 위치+반지름 AABB가 커버하는 그리드 셀 중 IsPathBlocked==1인 셀이 있으면 true 반환.
