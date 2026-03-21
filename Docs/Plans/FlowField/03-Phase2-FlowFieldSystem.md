# Phase 2: FlowFieldSystem (PathfindingSystem 대체)

**파일**: `Assets/Scripts/Server/Systems/Movement/PathfindingSystem.cs` → 재작성

---

## 신규 싱글톤: FlowFieldCacheData

```csharp
// Shared/Singletons/FlowFieldCacheData.cs
public struct FlowFieldCacheData : IComponentData
{
    // SpatialMaps 패턴: NativeContainer를 IComponentData에 직접 저장 (Persistent 할당)
    public NativeArray<byte> SmallFieldPool;   // maxFields * gridCellCount
    public NativeArray<byte> LargeFieldPool;
    public NativeHashMap<int, int> SmallKeyToPoolIndex;  // destinationKey → poolIndex (메인 스레드 전용, Parallel 불필요)
    public NativeHashMap<int, int> LargeKeyToPoolIndex;
    public int GridCellCount;

    [MarshalAs(UnmanagedType.U1)]
    public bool IsGridStale;  // 캐시 무효화 플래그 (RefRW 접근 시 Burst blittable 필수)

    // LRU 교체 추적
    public NativeArray<uint> SmallFieldLastUsedFrame;  // maxFields
    public NativeArray<uint> LargeFieldLastUsedFrame;  // maxFields
}
```

- FlowFieldSystem이 `OnCreate`에서 Persistent 할당, **`OnDestroy`에서 모든 NativeContainer Dispose**
- FlowFieldSteeringSystem이 ReadOnly로 조회
- NativeContainer 안에 NativeArray 중첩 불가 (Burst 제약) → Flat 배열 + poolIndex 오프셋

---

## 워커 메모리 관리

BFS 병렬 Job 최대 8개이므로 워커 메모리(NativeQueue, visited, cost map) 8세트 필요.
기존 `PathfindingSystem`이 시스템 필드로 관리하는 패턴(`_queries`, `_polygonBuffers` 등)을 따라, **FlowFieldSystem의 시스템 필드로 관리**한다 (FlowFieldCacheData 싱글톤에 포함하지 않음).

- `OnCreate`에서 8세트 Persistent 할당
- `OnDestroy`에서 Dispose

---

## 3-Phase 실행

### Phase 1: Collect (메인 스레드)

1. `IsPathDirty=true`인 유닛 수집
2. 각 유닛의 목적지를 그리드 셀로 변환 (`GridUtility.WorldToGrid`)
3. **FlyingTag 유닛** → `IsPathDirty=false`, `MovementWaypoints.Current=Destination` (직선 이동)
4. 유닛의 `GridPathfindingSize.CellPadding`에 따라 Small/Large 캐시 분기
5. 고유 목적지 셀 목록 추출 + 캐시 히트/미스 분류 (Small/Large 별도)
5. passability 맵 스냅샷 2종 생성 (Small/Large)
6. **캐시 무효화 프레임 스킵**: `IsGridStale=true`이면 dirty 유닛 계산을 스킵, `IsGridStale=false`로 리셋만 수행 → 다음 프레임(그리드 갱신 후)에 재계산

### Phase 2: Compute (병렬 IJob)

- 캐시 미스 목적지에 대해 `FlowFieldComputeJob` 실행
- 목적지당 1개 Job, 최대 8개 병렬
- `[BurstCompile]` 완전 호환

### Phase 3: Apply (메인 스레드)

1. 유닛에 `FlowFieldRef` 할당 (목적지 셀 키 저장)
2. `IsPathDirty=false`
3. `MovementWaypoints` 활성화
4. **Partial Path 판정**: 유닛 현재 셀의 방향이 None(255)이면 도달 불가
   - BFS cost map에서 "유닛 현재 위치에서 도달 가능한 셀 중 BFS cost가 가장 작은 셀"을 찾아 해당 셀까지 이동 (BFS 거리 기준)
   - 도달 가능 셀도 없으면 유닛 정지 + `IsPathPartial=true`

---

## 캐시 상세

- Flat `NativeArray<byte>` 풀: `maxFields * gridCellCount`
  - `poolIndex * gridCellCount` ~ `(poolIndex+1) * gridCellCount` 범위가 해당 필드
- `NativeHashMap<int, int>`: destinationKey → poolIndex (메인 스레드 전용)
- 유닛 크기별 2개 캐시 (Small/Large), 각각 별도 풀 — `GridPathfindingSize.CellPadding`으로 분기
- 최대 32 필드 (32 * 10KB = 320KB, 100x100 기준)
- LRU 방식 필드 교체: `LastUsedFrame` 배열에서 가장 오래된 슬롯을 교체 대상으로 선택
- 그리드 변경 시 전체 캐시 무효화 (HashMap Clear + LastUsedFrame 초기화)

---

## 의존성

```csharp
[UpdateInGroup(typeof(SimulationSystemGroup))]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateAfter(typeof(GridObstacleResponseSystem))]
```
- `UnifiedTargetingSystem`: `[UpdateBefore(typeof(FlowFieldSystem))]`로 변경 (기존 PathfindingSystem → FlowFieldSystem)

---

## 체크리스트

- [ ] `FlowFieldCacheData` 싱글톤 구현 + Persistent 할당 + `IsGridStale`에 `[MarshalAs(UnmanagedType.U1)]`
- [ ] LRU 추적 필드 (`SmallFieldLastUsedFrame`, `LargeFieldLastUsedFrame`) 구현
- [ ] 워커 메모리 8세트 시스템 필드로 Persistent 할당
- [ ] Collect Phase: dirty 유닛 수집, 목적지 셀 변환, Flying 처리
- [ ] Compute Phase: `FlowFieldComputeJob` 병렬 BFS
- [ ] Apply Phase: FlowFieldRef 할당, Partial Path 판정 (BFS 거리 기준)
- [ ] 캐시 무효화 프레임 스킵 메커니즘
- [ ] LRU 교체 로직 (LastUsedFrame 기반)
- [ ] 시스템 의존성 attribute 설정 (`UpdateInGroup`, `WorldSystemFilter`, `UpdateAfter`)
- [ ] `OnDestroy`에서 FlowFieldCacheData + 워커 메모리 전체 Dispose
