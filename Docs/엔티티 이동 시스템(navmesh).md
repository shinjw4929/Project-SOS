# 엔티티 이동 시스템(navmesh)

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
PathfindingSystem → NavMeshQuery 8개 병렬 IJob 경로 계산 + Funnel 알고리즘 → PathWaypoint 버퍼 (최대 512개/프레임)
    ↓
PathFollowSystem → MovementWaypoints.Current/Next 공급
    ↓
PredictedMovementSystem → LocalTransform 직접 이동 (SpatialMaps.MovementMap 사용)
    ↓
MovementArrivalSystem → 도착 판정 → 이동 정지 + Intent.Idle 전환
```

# 핵심 설계 포인트

1. 서버 권위 모델: 모든 이동 계산은 서버에서 수행, 클라이언트는 Ghost 보간으로 시각화
2. 3계층 구조: MovementGoal(명령) → PathWaypoint(계획) → MovementWaypoints(실행)
3. Kinematic 이동: PhysicsVelocity가 아닌 LocalTransform.Position 직접 수정
4. 대역폭 최적화: MovementWaypoints/MovementGoal 필드는 서버 전용 ([GhostField] 없음), PhysicsVelocity도 동기화 안 함 (Quantization=0). Ghost enabled 상태와 LocalTransform만 동기화
5. 공간 분할 충돌 회피: SpatialMaps.MovementMap(셀 크기 3.0f)을 사용한 Entity 기반 Separation
6. AttackMove 지원: 이동 중 적 자동 감지 (Intent.AttackMove 상태)

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
- IsPathDirty=true, CurrentWaypointIndex=0
- UnitIntentState = rpc.IsAttackMove ? Intent.AttackMove : Intent.Move
- AggroTarget 초기화 (공격 대상 제거)
- MovementWaypoints 활성화 (SetComponentEnabled)

---

### 이동 시스템 (Server/Systems/Movement/)

파일: NavMeshObstacleSpawnSystem.cs
그룹: SimulationSystemGroup
역할: 건물 생성 시 NavMeshObstacle GameObject 동적 생성
- NeedsNavMeshObstacle 태그 감지 → NavMeshObstacle 생성
- Carving 설정: carveOnlyStationary=true, carvingTimeToStationary=0.5초 (스파이크 방지)
- 건물 내부 엔티티(유닛/적) 자동 밀어내기: 건물 footprint + ObstacleRadius + 0.3f 바깥으로 위치 이동
- 8m 반경 내 이동 중인 유닛/적 경로 무효화 (IsPathDirty=true)
---
파일: NavMeshObstacleCleanupSystem.cs
그룹: SimulationSystemGroup (UpdateAfter: ServerDeathSystem)
역할: 건물 파괴 시 NavMeshObstacle GameObject 제거 + 주변 경로 무효화 + Dormant 적 깨우기
- NavMeshObstacleReference(Cleanup 컴포넌트)로 파괴된 건물 감지
- GameObject.transform.position에서 위치 획득 (Cleanup 엔티티는 LocalTransform 없음)
- EnemyTag: 12m 반경 내 Dormant 적 즉시 `EnemyContext.Idle`로 전환 + IsPathPartial 적 경로 무효화
- UnitTag: 12m 반경 내 IsPathPartial 유닛 경로 무효화
- NavMeshObstacle GameObject 파괴 + Cleanup 컴포넌트 제거
---
파일: PathfindingSystem.cs
그룹: SimulationSystemGroup (UpdateAfter: NavMeshObstacleSpawnSystem)
타입: `partial struct PathfindingSystem : ISystem` (unmanaged)
역할: IsPathDirty=true인 유닛 감지 → NavMeshQuery 8개 병렬 IJob → PathWaypoint 버퍼 채우기
- **3단계 파이프라인**: Collect(메인) → Compute(8 IJob 병렬) → Apply(메인)
  - Phase 1 Collect: IsPathDirty 엔티티를 PathRequest 배열에 수집 (초기 1024, 동적 2배 확장)
  - Phase 2 Compute: PathComputeJob 8개를 Schedule, 각 워커가 비겹침 인덱스 범위 처리
  - Phase 3 Apply: 결과를 ComponentLookup으로 PathWaypoint 버퍼, MovementGoal, MovementWaypoints에 반영
- **NavMeshQuery 병렬화**: 8개 독립 NavMeshQuery (struct 복사, IntPtr 핸들 공유 → 안전)
- **Funnel 알고리즘**: `NavMeshPathUtils.FindStraightPath`로 폴리곤 경로를 직선 웨이포인트로 변환
- **lazy 초기화**: NavMeshQuery 8개는 NavMeshWorld가 유효해진 후 생성
- Agent ID 캐싱으로 NavMesh.GetSettingsByIndex 호출 최소화
- `MapLocation`으로 시작/끝 위치를 NavMesh 위에 매핑 (SampleExtent: 5.0f)
- **Partial Path 처리**: 마지막 폴리곤 != 목적지 폴리곤 감지 → 마지막 웨이포인트(도달 불가능한 endPos) 제거 → `MovementGoal.IsPathPartial = true` 설정
- ProcessFirstWaypoint: Look-ahead 로직으로 지나친 웨이포인트 스킵 (값 기반 ref 시그니처)
- 최대 경로 길이: 64개, 폴리곤 노드 풀: 256개, 워커 수: 8개
- **Persistent 메모리**: 모든 NativeArray를 Persistent로 할당, 프레임 간 재사용 (GC 0)
---
파일: PathFollowSystem.cs
그룹: SimulationSystemGroup (UpdateAfter: PathfindingSystem, UpdateBefore: PredictedMovementSystem)
역할: PathWaypoint 버퍼 관리 및 MovementWaypoints.Next 공급
- 동기화 체크: PredictedMovementSystem이 웨이포인트 도착 시 인덱스 증가
- Next 웨이포인트 미리 채워 코너링/부드러운 전환 지원
---
파일: PredictedMovementSystem.cs
그룹: SimulationSystemGroup (UpdateAfter: PathfindingSystem)
역할: 핵심 이동 시스템
- Kinematic 방식: LocalTransform.Position 직접 수정
- 가속/감속 적용 (MovementDynamics)
- **Entity 기반 Separation**: SpatialMaps.MovementMap 사용 (셀 크기 3.0f)
  - 적-유닛 간 충돌 회피: 항상 활성화
  - 유닛-유닛 간 충돌 회피: 둘 다 Gather 상태면 무시
- **공격 중 Separation 유지**: `IgnoreComponentEnabledState` + `EnabledRefRW<MovementWaypoints>`로 MovementWaypoints 비활성화 엔티티도 쿼리에 포함. 이동만 스킵하고 Separation은 계속 적용.
- **비선형 Separation Force**: `forceMag = overlap * (1 + overlapRatio * 3)` — 깊이 침투 시 기하급수적 반발
- **Entity Hard Constraint**: 실제 반경(마진 0.3f 제외) 기준 겹침 시 위치 직접 보정 (hardPush, obstacleRadius.Radius로 크기 제한)
- 벽 충돌 미끄러짐 처리 (Raycast + PointDistance)
- 벽 충돌 안전망: `ClampToWall` static 메서드로 이동 후 + Entity push 후 벽 관통 재검사
- Separation 진동 감지: 최종 목적지 확장 반경(2배) 내에서 밀려나는 경우 즉시 정지
---
파일: MovementArrivalSystem.cs
그룹: SimulationSystemGroup (UpdateAfter: PredictedMovementSystem)
역할: 도착 판정 및 상태 전환
- 1차 도착 조건: 거리 < ArrivalRadius && !HasNext
- 2차 도착 조건: 거리 < ArrivalRadius*2 && !HasNext && 목적지 방향으로 이동하지 않음 (Separation 진동 포착)
- 적(EnemyTag): MovementWaypoints 비활성화, 속도 0
- 유닛(UnitTag): 추가로 Intent.Move → Intent.Idle 전환 (자동 타겟팅 활성화)

## Authoring (Authoring/Movement/)

| 파일 | 베이킹 컴포넌트 |
| --- | --- |
| MovementAuthoring.cs | MovementDynamics, MovementGoal, MovementWaypoints(비활성화), PathWaypoint 버퍼, NavMeshAgentConfig, Kinematic Mass (Rigidbody 없을 때만) |
| UnitMovementAuthoring.cs | UnitIntentState, UnitActionState, UnitCommand 버퍼. RequireComponent(MovementAuthoring) |

### MovementAuthoring 인스펙터 설정

| 필드 | 기본값 | 설명 |
| --- | --- | --- |
| MaxSpeed | 10.0 | 최대 이동 속도 (m/s) |
| Acceleration | 180.0 | 가속도 (m/s²) |
| Deceleration | 240.0 | 감속도 (m/s²) |
| RotationSpeed | 12.0 | 회전 속도 (rad/s) |
| ArrivalRadius | 0.5 | 도착 판정 반경 |
| AgentTypeIndex | 0 | Unity Navigation Agents 탭 순서 |

## 유틸리티

### Server/Utilities/

| 파일 | 역할 |
| --- | --- |
| NavMeshPathUtils.cs | Funnel 알고리즘(String-Pulling) 기반 폴리곤 경로 → 직선 웨이포인트 변환. `[BurstCompile]` 적용. `NavMeshQuery.GetPortalPoints`로 포탈 에지 획득, XZ 평면 Cross Product로 funnel 좁히기 수행 |

### Shared/Utilities/

| 파일 | 역할 |
| --- | --- |
| MovementMath.cs | 감속 거리 계산(CalculateSlowingDistance), 목표 속도 계산(CalculateTargetSpeed), 가속/감속 적용(CalculateNewSpeed) |
| SpatialHashUtility.cs | 공간 분할 해시 계산 (중앙 집중화) |

### SpatialHashUtility 상수

| 상수 | 값 | 용도 |
| --- | --- | --- |
| TargetingCellSize | 10.0f | 타겟팅용 셀 크기 (적→아군, 유닛→적) |
| MovementCellSize | 3.0f | 이동/충돌 회피용 셀 크기 |
| CapacityMultiplier | 1.5f | 해시 충돌 방지 여유 계수 |

### SpatialHashUtility 주요 함수

| 함수 | 역할 |
| --- | --- |
| GetCellHash(pos, cellSize) | 위치 기반 셀 해시 계산 |
| GetCellHash(pos, xOff, zOff, cellSize) | 오프셋 적용 셀 해시 (인접 셀 탐색용) |
| GetCellRange(pos, radius, cellSize, out min, out max) | 대형 유닛 AABB 셀 범위 계산 |
| IsLargeEntity(radius, cellSize) | 대형 유닛 여부 (radius > cellSize * 0.5f) |

## 컴포넌트 (Shared/Components/Movement/)

| 파일 | 역할 |
| --- | --- |
| MovementGoal.cs | 최종 목적지(Destination), 경로 재계산 플래그(IsPathDirty), 웨이포인트 인덱스(CurrentWaypointIndex, TotalWaypoints), Partial 경로 플래그(IsPathPartial), 목적지 설정 시간(DestinationSetTime), 마지막 위치 체크(LastPositionCheck, LastPositionCheckTime), Dormant 깨어남 시간(DormantWakeTime) |
| MovementWaypoints.cs | 현재 이동 목표(Current), 다음 지점(Next), HasNext, ArrivalRadius. IEnableableComponent로 이동 중/정지 상태 토글 |
| MovementDynamics.cs | 유닛 이동 파라미터: MaxSpeed, Acceleration, Deceleration, RotationSpeed |
| NavMeshAgentConfig.cs | Unity NavMesh Agent Type 인덱스 참조 (유닛 크기별 경로 계산) |

### Ghost 동기화 필드

| 컴포넌트 | 동기화 | 비동기화 필드 (서버 전용) |
| --- | --- | --- |
| MovementGoal | enabled 상태만 | Destination, IsPathDirty, CurrentWaypointIndex, TotalWaypoints, IsPathPartial, DestinationSetTime, LastPositionCheck, LastPositionCheckTime, DormantWakeTime |
| MovementWaypoints | enabled 상태만 | Current, Next, HasNext, ArrivalRadius |
| PhysicsVelocity | 없음 (Quantization=0) | Linear, Angular (PhysicsVelocityGhostOverride) |

**대역폭 최적화 근거**: 이동 시스템(PathfindingSystem, PathFollowSystem, PredictedMovementSystem)은 전부 서버 전용. 클라이언트는 Ghost 보간된 LocalTransform만으로 시각화하므로 이동 관련 필드 동기화 불필요. Ghost당 ~53바이트 절감.

## 버퍼 (Shared/Buffers/)

| 파일 | 역할 |
| --- | --- |
| PathWaypoint.cs | NavMesh 경로 계산 결과 저장. 서버 전용 (클라이언트 동기화 안함 - 대역폭 최적화) |

## 싱글톤 (Shared/Singletons/)

| 파일 | 역할 |
| --- | --- |
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
NavMeshObstacleSpawnSystem
    → 건물 NavMeshObstacle 생성, 주변 경로 무효화
    ↓
PathfindingSystem (UpdateAfter: NavMeshObstacleSpawnSystem)
    → Phase 1: IsPathDirty=true 수집 → PathRequest 배열 (초기 1024, 동적 확장)
    → Phase 2: PathComputeJob × 8 병렬 Schedule (NavMeshQuery 8개)
    → Phase 3: 결과 Apply (ComponentLookup으로 PathWaypoint 버퍼 채우기)
    ↓
PathFollowSystem (UpdateAfter: PathfindingSystem, UpdateBefore: PredictedMovementSystem)
    → CurrentWaypointIndex 증가
    → MovementWaypoints.Next 공급
    ↓
PredictedMovementSystem (UpdateAfter: PathfindingSystem)
    → LocalTransform.Position 직접 수정
    → SpatialMaps.MovementMap 기반 Separation
    → 벽 충돌 미끄러짐
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
bool shouldCollide = iAmEnemy || isEnemy || (!iAmGathering && !isGathering);

// 적-유닛: 항상 충돌 회피
// 유닛-유닛: 둘 다 Gather가 아닐 때만 충돌 회피
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

// Hard constraint: 실제 반경(마진 0.3f 제외) 기준 겹침 위치 보정
float hardCombinedR = myRadius + otherRadius;
if (dist < hardCombinedR)
    hardPush += (toOther / dist) * (hardOverlap * 0.5f);

// hardPush 크기 제한: 밀집 시 순간이동 방지
float maxPush = obstacleRadius.Radius;
if (lengthsq(hardPush) > maxPush * maxPush)
    hardPush = normalize(hardPush) * maxPush;
```

### 벽 충돌 처리

1. **Raycast**: 이동 방향으로 벽 감지 → 속도 벡터에서 법선 성분 제거 (미끄러짐)
2. **PointDistance**: 주변 전방향 충돌 검사 → 겹침 시 밀어내기
3. **ClampToWall (안전망)**: `transform.Position` 업데이트 후 + Entity hardPush 후, 벽과 겹침(overlap > 0.05f)이면 SurfaceNormal 방향으로 밀어내기. Entity push가 벽 안으로 밀 수 있으므로 2회 검사.

```csharp
// 유닛: 속도 절대값 유지 (미끄러지면서도 동일 속력)
// 적: 기존 로직 (속도 감소 가능)
// 위치 보정: flying이 아닌 엔티티만 적용
```