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
PathfindingSystem → NavMeshQuery 기반 경로 계산 + Funnel 알고리즘 → PathWaypoint 버퍼 (시간 기반 제한: 1ms/프레임)
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
4. 대역폭 최적화: 전체 경로 대신 Current/Next 두 지점만 동기화
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
- 8m 반경 내 이동 중인 유닛 경로 무효화 (IsPathDirty=true)
- 프레임당 최대 2개 스폰 제한
---
파일: NavMeshObstacleCleanupSystem.cs
그룹: SimulationSystemGroup (UpdateAfter: ServerDeathSystem)
역할: 건물 파괴 시 NavMeshObstacle GameObject 제거 (NavMeshObstacleReference로 참조)
---
파일: PathfindingSystem.cs
그룹: SimulationSystemGroup (UpdateAfter: NavMeshObstacleSpawnSystem)
타입: `partial struct PathfindingSystem : ISystem` (unmanaged)
역할: IsPathDirty=true인 유닛 감지 → NavMeshQuery 기반 경로 계산 → PathWaypoint 버퍼 채우기
- **NavMeshQuery 기반**: `BeginFindPath` → `UpdateFindPath` → `EndFindPath` → `GetPathResult` → Funnel 알고리즘
- **Funnel 알고리즘**: `NavMeshPathUtils.FindStraightPath`로 폴리곤 경로를 직선 웨이포인트로 변환 (Burst 호환)
- **시간 기반 제한**: 프레임당 최대 1.0ms (Stopwatch 기반 Time Slicing)
- **lazy 초기화**: NavMeshQuery는 NavMeshWorld가 유효해진 후 생성
- Agent ID 캐싱으로 NavMesh.GetSettingsByIndex 호출 최소화
- `MapLocation`으로 시작/끝 위치를 NavMesh 위에 매핑 (SampleExtent: 5.0f)
- Partial Path 지원: `PathQueryStatus.Partial` 플래그도 수용
- ProcessFirstWaypoint: Look-ahead 로직으로 지나친 웨이포인트 스킵
- 최대 경로 길이: 64개, 폴리곤 노드 풀: 256개
---
파일: PathFollowSystem.cs
그룹: SimulationSystemGroup (UpdateAfter: PathfindingSystem)
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
- 벽 충돌 미끄러짐 처리 (Raycast + PointDistance)
- 공격 중(Attacking) 상태면 이동 스킵
---
파일: MovementArrivalSystem.cs
그룹: SimulationSystemGroup (UpdateAfter: PredictedMovementSystem)
역할: 도착 판정 및 상태 전환
- 도착 조건: 거리 < ArrivalRadius && !HasNext && 속도 < 0.05
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
| MovementGoal.cs | 최종 목적지(Destination), 경로 재계산 플래그(IsPathDirty), 웨이포인트 인덱스(CurrentWaypointIndex, TotalWaypoints) |
| MovementWaypoints.cs | 현재 이동 목표(Current), 다음 지점(Next), HasNext, ArrivalRadius. IEnableableComponent로 이동 중/정지 상태 토글 |
| MovementDynamics.cs | 유닛 이동 파라미터: MaxSpeed, Acceleration, Deceleration, RotationSpeed |
| NavMeshAgentConfig.cs | Unity NavMesh Agent Type 인덱스 참조 (유닛 크기별 경로 계산) |

### Ghost 동기화 필드

| 컴포넌트 | 동기화 필드 | 비동기화 필드 (서버 전용) |
| --- | --- | --- |
| MovementGoal | Destination | IsPathDirty, CurrentWaypointIndex, TotalWaypoints |
| MovementWaypoints | Current, Next, HasNext, ArrivalRadius | - |

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
    → IsPathDirty=true 감지
    → NavMeshQuery: BeginFindPath → UpdateFindPath → EndFindPath (시간 제한: 1ms/프레임)
    → NavMeshPathUtils.FindStraightPath (Funnel 알고리즘)
    → PathWaypoint 버퍼 채우기
    ↓
PathFollowSystem (UpdateAfter: PathfindingSystem)
    → CurrentWaypointIndex 증가
    → MovementWaypoints.Next 공급
    ↓
PredictedMovementSystem (UpdateAfter: PathfindingSystem)
    → LocalTransform.Position 직접 수정
    → SpatialMaps.MovementMap 기반 Separation
    → 벽 충돌 미끄러짐
    ↓
MovementArrivalSystem (UpdateAfter: PredictedMovementSystem)
    → 도착 판정
    → MovementWaypoints 비활성화
    → Intent.Move → Intent.Idle 전환

```

## 충돌 회피 로직

### PredictedMovementSystem 상세

```csharp
// 충돌 회피 조건
bool shouldCollide = iAmEnemy || isEnemy || (!iAmGathering && !isGathering);

// 적-유닛: 항상 충돌 회피
// 유닛-유닛: 둘 다 Gather가 아닐 때만 충돌 회피
// 적-적: 항상 충돌 회피
```

### 벽 충돌 처리

1. **Raycast**: 이동 방향으로 벽 감지 → 속도 벡터에서 법선 성분 제거 (미끄러짐)
2. **PointDistance**: 주변 전방향 충돌 검사 → 겹침 시 밀어내기

```csharp
// 유닛: 속도 절대값 유지 (미끄러지면서도 동일 속력)
// 적: 기존 로직 (속도 감소 가능)
```