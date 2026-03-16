# NavMesh → Grid Flow Field 경로 탐색 전환 계획

## Context

벽(Wall) 물리 콜라이더(radius=0.4)와 그리드 점유 영역(2x2=2.0x2.0) 사이 불일치로 인해 Tank/EnemyBig 등 대형 유닛이 벽 사이 갭을 물리적으로 통과하는 버그가 발생. 근본 원인은 NavMesh/Grid/Physics 3중 데이터 소스의 불일치이며, NavMeshObstacle(managed GameObject)이 DOTS 패러다임에도 맞지 않음. 그리드를 경로 탐색의 단일 진실 소스로 만들고, Flow Field로 전환하여 다수 유닛 이동 성능도 확보한다.

**기획 원칙**: 유닛 간 밀기(displacement) 불가 — hardPush는 이미 제거됨 (soft separation만 사용).

---

## 설계 결정 요약

| 항목 | 결정 | 근거 |
|------|------|------|
| 알고리즘 | **Flow Field** (BFS 기반) | 목적지당 1회 계산 → 다수 유닛 공유, RTS에 최적 |
| 유닛 크기 | 2종 passability 맵 (Small/Large) | Small(패딩 0)은 좁은 통로 통과, Large(패딩 1)는 벽 갭 차단 |
| 경로 스무딩 | 불필요 | Flow Field는 셀 단위로 방향을 제공, 자연스러운 곡선 이동 |
| 벽 충돌 | PredictedMovementSystem 기존 Physics 로직 유지 | 서브셀 정밀도 필요, 경로만 Grid로 전환 |
| Flying 유닛 | 장애물 무시 직선 이동 | Flow Field 조회 스킵, 목적지 방향으로 직진 |
| 동적 장애물 | GridCell 직접 참조 + 캐시 무효화 | 건물 건설/파괴 시 모든 캐시 필드 폐기 |
| 하위 호환 | MovementWaypoints 유지 | FlowFieldSteeringSystem이 매 프레임 가상 웨이포인트 주입 → PredictedMovementSystem/MovementArrivalSystem 최소 변경 |

---

## Flow Field 동작 원리

```
1. 유닛이 MoveRequestRpc 수신 → MovementGoal.Destination 설정, IsPathDirty=true

2. FlowFieldSystem (매 프레임):
   ├─ Phase 1: IsPathDirty=true인 유닛 수집 → 고유 목적지 셀 목록 추출
   ├─ Phase 2: 캐시 미스인 목적지에 대해 BFS 계산 (병렬 IJob)
   │   └─ BFS: 목적지 셀에서 출발, 인접 passable 셀로 확산, 각 셀에 방향(byte) 기록
   └─ Phase 3: 유닛에 FlowFieldRef(목적지 셀 키) 할당, IsPathDirty=false

3. FlowFieldSteeringSystem (매 프레임, 이동 중 유닛):
   ├─ 유닛의 현재 셀 → Flow Field에서 방향 조회
   ├─ MovementWaypoints.Current = 현재 위치 + 방향 * CellSize
   └─ PredictedMovementSystem이 기존대로 Current를 향해 이동

4. 도착: MovementArrivalSystem이 목적지 거리 판정 (기존 로직)
```

**캐시 전략**:
- Flat `NativeArray<byte>` 풀 (`maxFields * gridCellCount`) + `NativeHashMap<int, int>` (destinationKey → poolIndex)
  - NativeContainer 안에 NativeArray 중첩 불가 (Burst 제약)
  - poolIndex * gridCellCount ~ (poolIndex+1) * gridCellCount 범위가 해당 필드
- 유닛 크기별 2개 캐시 (Small/Large), 각각 별도 풀
- 최대 32 필드 캐시 (32 * 10KB = 320KB)
- LRU 방식 필드 교체
- 그리드 변경 시 (건물 건설/파괴) → **전체 캐시 무효화** (BFS가 ~0.1ms로 빠르므로 재계산 비용 무시 가능)
- **타이밍**: passability 스냅샷은 이전 프레임 LateSimulation(GridOccupancyEventSystem) 결과. 건물 건설 후 1프레임 지연 있으나 실질적 영향 없음

---

## Phase 0: Grid 유틸리티 확장

**파일**: `Assets/Scripts/Shared/Utilities/GridUtility.cs`

추가 메서드:
- `IsPassable(NativeArray<byte> map, int x, int y, int gridSizeX, int gridSizeY)` — 범위 + 점유 체크
- `IsPassableForSize(... , int cellPadding, ...)` — 유닛 크기 고려 (주변 셀 확장 체크)
- `BuildPassabilityMap(DynamicBuffer<GridCell>, int2 gridSize, int cellPadding, NativeArray<byte> output)` — GridCell → byte 맵 변환 (0=passable, 1=blocked)

---

## Phase 1: Flow Field 코어 알고리즘

**신규 파일**: `Assets/Scripts/Server/Utilities/FlowFieldCore.cs`

```
[BurstCompile] struct FlowFieldCore:
  - ComputeField(NativeArray<byte> passabilityMap, int2 destination, int2 gridSize, NativeArray<byte> outputField)
  - BFS 구현: Queue(NativeQueue) + visited bitset
  - 8방향 확산, 각 셀에 방향(byte 0-7) 기록, 도달 불가 셀은 255(None)
  - 대각 이동 시 코너 차단 (인접 직교 셀이 막혀있으면 대각 확산 불가)
```

**방향 인코딩** (byte):
```
0=N, 1=NE, 2=E, 3=SE, 4=S, 5=SW, 6=W, 7=NW, 255=None(도달불가/목적지)
```

**워커 메모리** (Persistent, 재사용):
- BFS queue: NativeQueue<int2> (최대 10,000)
- visited bitset: NativeArray<byte> (1,250 bytes)
- cost map: NativeArray<ushort> (20,000 bytes) — BFS 거리 저장, 방향 결정용

---

## Phase 2: FlowFieldSystem (PathfindingSystem 대체)

**파일**: `Assets/Scripts/Server/Systems/Movement/PathfindingSystem.cs` → 재작성

### 신규 싱글톤: FlowFieldCache
```csharp
// Shared/Singletons/FlowFieldCache.cs
public struct FlowFieldCache : IComponentData
{
    // 런타임 데이터는 SystemState에 저장 (NativeContainer는 IComponentData에 불가)
}
```
실제 캐시 데이터는 FlowFieldSystem의 SystemState(Persistent NativeContainer)에 저장.

### Phase 1: Collect (메인 스레드)
- IsPathDirty=true인 유닛 수집
- 각 유닛의 목적지를 그리드 셀로 변환 (`GridUtility.WorldToGrid`)
- FlyingTag 유닛 → IsPathDirty=false 설정, MovementWaypoints.Current=Destination (직선)
- 고유 목적지 셀 목록 추출 + 캐시 히트/미스 분류
- passability 맵 스냅샷 2종 생성 (Small/Large)

### Phase 2: Compute (병렬 IJob)
- 캐시 미스 목적지에 대해 `FlowFieldComputeJob` 실행
- 목적지당 1개 Job, 최대 8개 병렬 (IJobParallelFor 또는 개별 IJob)
- `[BurstCompile]` 완전 호환

### Phase 3: Apply (메인 스레드)
- 유닛에 FlowFieldRef 할당 (목적지 셀 키 저장)
- IsPathDirty=false
- MovementWaypoints 활성화
- Partial Path 판정: 유닛 현재 셀의 방향이 None(255)이면 도달 불가
  - BFS cost map에서 목적지에 **가장 가까운 도달 가능 셀**을 찾아 해당 셀까지 이동 (NavMesh Partial Path 동등)
  - 도달 가능 셀도 없으면 유닛 정지 + IsPathPartial=true

**의존성**:
- `UpdateAfter(GridObstacleResponseSystem)`
- `UnifiedTargetingSystem`의 `UpdateBefore: PathfindingSystem` → `UpdateBefore: FlowFieldSystem`으로 변경

---

## Phase 2.5: FlowFieldSteeringSystem (PathFollowSystem 대체)

**파일**: `Assets/Scripts/Server/Systems/Movement/PathFollowSystem.cs` → 재작성

```
[UpdateAfter(FlowFieldSystem)]
[UpdateBefore(PredictedMovementSystem)]

매 프레임:
1. 이동 중 유닛(MovementWaypoints enabled) 순회
2. 유닛 현재 위치 → 그리드 셀 변환
3. FlowFieldRef.Key로 캐시에서 Flow Field 조회
4. 현재 셀의 방향(byte) → float3 방향 벡터 변환
5. MovementWaypoints.Current = 현재 위치 + 방향 * CellSize
6. MovementWaypoints.HasNext = false (매 프레임 갱신이므로 Next 불필요)
```

이 방식으로 PredictedMovementSystem은 기존대로 `MovementWaypoints.Current`를 향해 이동. **변경 없음**.

---

## Phase 3: NavMesh 장애물 시스템 대체

### GridObstacleResponseSystem (NavMeshObstacleSpawnSystem 대체)

**신규 파일**: `Assets/Scripts/Server/Systems/Movement/GridObstacleResponseSystem.cs`

- `NeedsNavMeshObstacle` 태그 감지 (재사용)
- 기능 1: 건물 footprint 내부 엔티티 밀어내기 (기존 `PushAndInvalidateNearbyPaths` 로직 이전)
- 기능 2: 주변 8m 유닛 `IsPathDirty=true`
- 기능 3: **Flow Field 캐시 전체 무효화** 트리거
- **제거**: GameObject 생성, NavMeshObstacleReference managed component
- ISystem + `[BurstCompile]` 가능

### GridObstacleCleanupSystem (NavMeshObstacleCleanupSystem 대체)

**신규 파일**: `Assets/Scripts/Server/Systems/Movement/GridObstacleCleanupSystem.cs`

- `GridOccupancyCleanup` (이미 존재) 활용
- 건물 파괴 시 주변 Partial Path 무효화 + Dormant 적 깨우기
- **Flow Field 캐시 전체 무효화** 트리거
- **제거**: `NavMeshObstacleReference`, `GameObject.Destroy`

---

## Phase 4: Authoring/컴포넌트 정리

| 파일 | 변경 |
|------|------|
| `Authoring/Movement/MovementAuthoring.cs` | `AgentTypeIndex` → `PathfindingSize` (Small/Large), `GridPathfindingSize` 베이킹 |
| NEW `Shared/Components/Movement/GridPathfindingSize.cs` | `byte CellPadding` (0=Small, 1=Large) |
| NEW `Shared/Components/Movement/FlowFieldRef.cs` | `int Key` (캐시 조회 키, 목적지 셀 인덱스) |
| REMOVE `Shared/Components/Movement/NavMeshAgentConfig.cs` | GridPathfindingSize로 대체 |
| REMOVE `Shared/Components/Data/NavMeshObstacleProxy.cs` | managed component 제거 |
| REMOVE `Server/Utilities/NavMeshPathUtils.cs` | Funnel 알고리즘 불필요 |
| REMOVE `Shared/Buffers/PathWaypoint.cs` | Flow Field 직접 스티어링으로 웨이포인트 버퍼 불필요 |
| MODIFY `Shared/Components/Movement/MovementGoal.cs` | `CurrentWaypointIndex`, `TotalWaypoints` 필드 제거 (Flow Field에서 미사용) |
| MODIFY `Shared/Systems/Grid/ObstacleGridInitSystem.cs` | NavMesh 참조 제거 (태그 재사용) |

---

## Phase 5: 테스트

### EditMode
- FlowFieldCore.ComputeField: 빈 그리드, 장애물 우회, 대각 이동, 도달 불가 셀
- GridUtility.IsPassableForSize: Large 유닛 셀 패딩 검증
- 캐시 무효화 동작

### PlayMode
- 유닛 이동 → Flow Field 생성 → 도착
- 다수 유닛 동일 목적지 → 필드 공유 확인
- 건물 건설 → 캐시 무효화 → 재경로
- 건물 파괴 → 재경로
- **핵심**: Tank/EnemyBig 벽 사이 통과 불가 확인
- Flying 유닛 벽 통과 확인
- 대량 유닛(50+) 동시 이동 성능

---

## 변경 파일 요약

| 작업 | 파일 |
|------|------|
| REWRITE | `Server/Systems/Movement/PathfindingSystem.cs` → FlowFieldSystem |
| REWRITE | `Server/Systems/Movement/PathFollowSystem.cs` → FlowFieldSteeringSystem |
| NEW | `Server/Utilities/FlowFieldCore.cs` |
| NEW | `Server/Systems/Movement/GridObstacleResponseSystem.cs` |
| NEW | `Server/Systems/Movement/GridObstacleCleanupSystem.cs` |
| NEW | `Shared/Components/Movement/GridPathfindingSize.cs` |
| NEW | `Shared/Components/Movement/FlowFieldRef.cs` |
| MODIFY | `Shared/Utilities/GridUtility.cs` |
| MODIFY | `Authoring/Movement/MovementAuthoring.cs` |
| MODIFY | `Shared/Systems/Grid/ObstacleGridInitSystem.cs` |
| REMOVE | `Server/Systems/Movement/NavMeshObstacleSpawnSystem.cs` |
| REMOVE | `Server/Systems/Movement/NavMeshObstacleCleanupSystem.cs` |
| REMOVE | `Server/Utilities/NavMeshPathUtils.cs` |
| REMOVE | `Shared/Components/Movement/NavMeshAgentConfig.cs` |
| REMOVE | `Shared/Components/Data/NavMeshObstacleProxy.cs` |

**변경 없음**: PredictedMovementSystem, MovementArrivalSystem, GridOccupancyEventSystem

---

## 후속 필요 사항 (구현 후)
- 문서 업데이트: `Docs/엔티티 이동 시스템(navmesh).md`, `Docs/시스템 그룹 및 의존성.md`, `Docs/코드베이스 구조.md`
- `BuildingUtility.cs`에서 `NeedsNavMeshObstacle` 활성화 경로 확인 (런타임 건물)
- `com.unity.ai.navigation` 패키지 제거 가능 여부 확인

---

## 성능 비교

| 항목 | NavMesh (현재) | Flow Field (전환 후) |
|------|---------------|---------------------|
| 100유닛 동일 목적지 | NavMeshQuery × 100회 | BFS × **1회** |
| 계산 단위 | **유닛당** (N유닛 = N회) | **목적지당** (동일 목적지 공유) |
| managed 객체 | 건물당 NavMeshObstacle GO | **0** |
| 장애물 반영 지연 | carving 0.5초 | **즉시** (GridCell 직접 참조) |
| 데이터 소스 | NavMesh + Grid + Physics (3중) | **Grid + Physics (2중)** |
| 캐시 메모리 | NavMesh bake (~수 MB) | 32 필드 × 10KB = **320KB** |
