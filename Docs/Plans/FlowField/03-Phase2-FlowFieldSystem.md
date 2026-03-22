# Phase 2: FlowFieldSystem (PathfindingSystem 대체)

**파일**: `Assets/Scripts/Server/Systems/Movement/PathfindingSystem.cs` → 재작성

---

## 신규 싱글톤: FlowFieldCacheData

```csharp
// Shared/Singletons/FlowFieldCacheData.cs
public struct FlowFieldCacheData : IComponentData
{
    // --- Flow Field 캐시 ---
    public NativeArray<byte> SmallFieldPool;   // maxFields * gridCellCount
    public NativeArray<byte> LargeFieldPool;
    public NativeHashMap<int, int> SmallKeyToPoolIndex;  // destinationKey → poolIndex (메인 스레드 전용)
    public NativeHashMap<int, int> LargeKeyToPoolIndex;
    public int GridCellCount;

    [MarshalAs(UnmanagedType.U1)]
    public bool IsGridStale;  // 캐시 무효화 플래그

    // LRU 교체 추적
    public NativeArray<uint> SmallFieldLastUsedFrame;  // maxFields
    public NativeArray<uint> LargeFieldLastUsedFrame;  // maxFields

    // --- Passability 맵 캐시 ---
    // 그리드 변경(IsGridStale) 시에만 재생성, 평상시 재사용
    public NativeArray<byte> SmallPassabilityMap;  // gridCellCount (패딩 0)
    public NativeArray<byte> LargePassabilityMap;  // gridCellCount (패딩 1)
}
```

- FlowFieldSystem이 `OnCreate`에서 Persistent 할당, **`OnDestroy`에서 모든 NativeContainer Dispose**
- FlowFieldSteeringSystem이 ReadOnly로 조회
- NativeContainer 안에 NativeArray 중첩 불가 (Burst 제약) → Flat 배열 + poolIndex 오프셋

### destinationKey 생성

```csharp
// 목적지 그리드 좌표 → 1D 인덱스 (Small/Large 캐시 각각 별도 HashMap이므로 네임스페이스 충돌 없음)
int destinationKey = destCell.y * gridSizeX + destCell.x;
```

---

## 워커 메모리 관리

BFS 병렬 Job 최대 8개이므로 워커 메모리(NativeQueue, visited, cost map) 8세트 필요.
기존 `PathfindingSystem`이 시스템 필드로 관리하는 패턴을 따라, **FlowFieldSystem의 시스템 필드로 관리**한다.

- `OnCreate`에서 8세트 Persistent 할당
- `OnDestroy`에서 Dispose

---

## 실행 흐름

### Phase 0: Passability 맵 갱신 (메인 스레드, 조건부)

`IsGridStale=true`일 때만 실행. 평상시(그리드 미변경)에는 스킵.

1. Flow Field 캐시 전체 무효화 (HashMap Clear + LastUsedFrame 초기화)
2. `BuildPassabilityMap(cells, gridSize, 0, SmallPassabilityMap)` — Small용 재생성
3. `BuildPassabilityMap(cells, gridSize, 1, LargePassabilityMap)` — Large용 재생성
4. `IsGridStale=false`

> **비용**: 40,000셀 × 2맵 = 80,000 byte 순회. 건물 건설/파괴 시에만 발생하므로 무시 가능.
> **타이밍**: GridObstacleResponseSystem이 같은 프레임 앞서 실행되어 IsPathBlocked를 직접 수정하므로, passability 맵 재빌드 시 최신 상태가 반영된다. 프레임 스킵 불필요.

### Phase 1: Collect (메인 스레드)

1. `IsPathDirty=true`인 유닛 수집 — 쿼리에 `WithNone<FlyingTag>` 필터 적용 (Flying 유닛은 별도 처리)
2. 각 유닛의 목적지를 그리드 셀로 변환 (`GridUtility.WorldToGrid`)
   - **목적지 범위 검증**: `destCell.x < 0 || destCell.x >= gridSize.x || destCell.y < 0 || destCell.y >= gridSize.y`이면 BFS 스킵, `IsPathPartial=true` 설정
3. **FlyingTag 유닛** (별도 쿼리): `IsPathDirty=true`인 Flying 유닛 → `IsPathDirty=false`, `MovementWaypoints.Current=Destination`, `HasNext=false`, `MovementWaypoints enabled=true` (직선 이동)
4. 유닛의 `GridPathfindingSize.CellPadding`에 따라 Small/Large 캐시 분기
5. 고유 목적지 셀 목록 추출 + 캐시 히트/미스 분류 (Small/Large 별도)
6. BFS 계산 시 캐싱된 `SmallPassabilityMap` / `LargePassabilityMap` 사용
7. **GridSettings 접근**: `var gridSettings = SystemAPI.GetSingleton<GridSettings>();` → `gridSizeX = gridSettings.GridSize.x`로 destinationKey 계산

### Phase 2: Compute (병렬 IJob)

- 캐시 미스 목적지에 대해 `FlowFieldComputeJob` 실행
- **캐시 풀 → outputField 전달**: `NativeArray<byte>.GetSubArray(poolIndex * gridCellCount, gridCellCount)`로 Flat 풀에서 서브어레이를 추출하여 `FlowFieldCore.ComputeField`의 `outputField` 파라미터에 전달
- 워커 8세트를 활용한 배치 처리: `batchSize = (missCount + 7) / 8`, 각 워커가 여러 목적지를 순차 처리
- 캐시 미스가 8개 이하면 목적지당 1개 워커, 초과 시 한 워커가 여러 목적지를 순차 처리
- **워커 메모리 재초기화**: 각 목적지 BFS 실행 전 워커 메모리를 초기화해야 한다 (이전 BFS 잔여 데이터 방지)
  - `queue.Clear()` — NativeQueue 비우기
  - `visited` — `MemSet(0)` (byte 배열)
  - `outputField` — `MemSet(255)` (방문 안 된 셀 = None)
  - `costMap` — `MemSet(ushort.MaxValue)` (미방문 = 최대 거리)
- `[BurstCompile]` 완전 호환

### Phase 3: Apply (메인 스레드)

1. **ComponentLookup 갱신**: Apply Phase 진입 시 `goalLookup.Update(ref state)`, `waypointsLookup.Update(ref state)` 호출 필수 (Compute Phase Job 완료 후 Lookup 데이터가 stale 될 수 있음)
2. 유닛에 `FlowFieldRef` 할당 (destinationKey 저장)
3. `IsPathDirty=false`
4. `MovementWaypoints` 활성화
5. **Partial Path 판정**: 유닛 현재 셀의 방향이 None(255)이면 도달 불가
   - 유닛 현재 셀 주변 **8방향 1단계**만 탐색하여 방향이 유효한(255가 아닌) 셀을 찾음
   - 유효 셀이 여러 개이면 **체비셰프 거리**(8방향 동일 거리)이므로 인덱스 순서대로 첫 번째 선택
   - 도달 가능 셀을 찾으면 해당 셀 중심을 `MovementWaypoints.Current`로 설정, `HasNext=false`
   - 8방향 모두 None(255)이면 유닛 정지: `MovementWaypoints` disabled + `IsPathPartial=true`
   - Partial Path 유닛은 FlowFieldSteeringSystem에서 스킵 (`MovementWaypoints` disabled 상태)

> **참고**: cost map이 아닌 Flow Field 출력(방향 배열)을 직접 스캔한다. 워커 메모리의 cost map은 Compute Phase에서 덮어씌워질 수 있으므로 Apply Phase에서 참조하지 않는다.

---

## 캐시 상세

- Flat `NativeArray<byte>` 풀: `maxFields * gridCellCount`
  - `poolIndex * gridCellCount` ~ `(poolIndex+1) * gridCellCount` 범위가 해당 필드
- `NativeHashMap<int, int>`: destinationKey → poolIndex (메인 스레드 전용)
- 유닛 크기별 2개 캐시 (Small/Large), 각각 별도 풀 — `GridPathfindingSize.CellPadding`으로 분기
- 최대 32 필드 × 2풀 = **총 64 필드** (64 * 40KB = **2.56MB**, 200x200 기준)
- LRU 방식 필드 교체: `LastUsedFrame` 배열에서 가장 오래된 슬롯을 교체 대상으로 선택
- 그리드 변경 시 전체 캐시 무효화 (HashMap Clear + LastUsedFrame 초기화)

---

## OnDestroy Dispose 대상

```
FlowFieldCacheData (8개):
  SmallFieldPool, LargeFieldPool,
  SmallKeyToPoolIndex, LargeKeyToPoolIndex,
  SmallFieldLastUsedFrame, LargeFieldLastUsedFrame,
  SmallPassabilityMap, LargePassabilityMap

워커 메모리 (8세트 × 3종 = 24개):
  NativeQueue<int2> × 8, NativeArray<byte> visited × 8, NativeArray<ushort> costMap × 8
```

---

## 의존성

```csharp
[UpdateInGroup(typeof(SimulationSystemGroup))]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateAfter(typeof(GridObstacleResponseSystem))]
```
- `UnifiedTargetingSystem`: `[UpdateBefore(typeof(FlowFieldSystem))]`로 변경

---

## 체크리스트

- [ ] `FlowFieldCacheData` 싱글톤 구현 + Persistent 할당 + `IsGridStale`에 `[MarshalAs(UnmanagedType.U1)]`
- [ ] LRU 추적 필드 구현
- [ ] 워커 메모리 8세트 시스템 필드로 Persistent 할당
- [ ] Passability 맵 캐시: `IsGridStale` 시에만 재생성 (프레임 스킵 불필요)
- [ ] Collect Phase: dirty 유닛 수집 (`WithNone<FlyingTag>`), 목적지 셀 변환 + 범위 검증, Flying 별도 처리, 캐싱된 passability 맵 사용
- [ ] Compute Phase: `FlowFieldComputeJob` 배치 처리 (missCount > 8 시 워커당 다중 목적지), 각 BFS 전 워커 메모리 재초기화 (queue.Clear, visited MemSet(0), outputField MemSet(255))
- [ ] Apply Phase: ComponentLookup.Update 호출, FlowFieldRef 할당, Partial Path 판정 (8방향 1단계 탐색, 체비셰프 거리, Flow Field 출력 스캔, cost map 미참조)
- [ ] destinationKey 생성: `destCell.y * gridSizeX + destCell.x`
- [ ] LRU 교체 로직
- [ ] 시스템 의존성 attribute 설정
- [ ] `OnDestroy`에서 NativeContainer 전수 Dispose (8 + 24 = 32개)
