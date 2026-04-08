# 성능 리뷰 체크리스트

> `/review-code` 스킬의 성능 에이전트가 참조하는 검토 항목.

## F-1. 메모리 할당

| 검토 항목 | 위반 조건 |
|-----------|-----------|
| Allocator 선택 | Job에 전달하는 NativeContainer에 `Allocator.Temp` 사용 (`TempJob` 사용해야 함). 프레임 범위 로컬 변수에 `TempJob`/`Persistent` 사용 |
| TempJob Dispose | `Allocator.TempJob` 할당 후 명시적 `Dispose()` 누락 (메모리 누수) |
| Persistent Dispose | `Allocator.Persistent` 할당 후 `OnDestroy`에서 `.IsCreated` 체크 + `Dispose()` 누락 |
| 매 프레임 할당 | `ToComponentDataArray`, `ToEntityArray` 등 Query 복사를 매 프레임 무조건 호출 (조건부 실행 가능 시 위반. 예: `ProductionProgressSystem`의 `anyCompletingThisFrame` 패턴 참조) |
| 불필요한 할당 | 결과를 사용하지 않는 NativeContainer 할당, 또는 기존 Persistent 컨테이너로 대체 가능한 Temp 재할당 |

## F-2. Job 스케줄링

| 검토 항목 | 위반 조건 |
|-----------|-----------|
| 불필요한 CompleteDependency | 같은 SystemGroup 내에서 `CompleteDependency`/`Dependency.Complete()` 호출 (`UpdateAfter`로 순서 보장 가능 시 위반) |
| 병렬화 미활용 | 독립적인 IJobEntity를 순차 스케줄링 (동일 컴포넌트 ReadWrite 충돌이 없으면 `ScheduleParallel` + `JobHandle.CombineDependencies`로 병렬화 가능) |
| Schedule vs Run | 엔티티 수가 충분한 IJobEntity에 `Run()` 사용 (`ScheduleParallel` 사용해야 함). 반대로 엔티티 1~2개 보장 시스템에 `ScheduleParallel` 오버헤드 |
| 의존성 체인 누락 | Job 핸들을 `state.Dependency`에 연결하지 않아 다음 시스템과 데이터 레이스 발생 |

## F-3. Lookup / 데이터 접근

| 검토 항목 | 위반 조건 |
|-----------|-----------|
| O(n²) 탐색 | 전체 엔티티 순회로 최근접/범위 탐색 수행 (SpatialMaps 사용 가능 시 위반. TargetingMap=10f, MovementMap=3f) |
| Lookup 과다 | 단일 시스템/Job에 Lookup 10개 이상 보유 시 분리 가능성 검토 (캐시 미스 증가) |
| Lookup ReadOnly 미적용 | 읽기 전용 Lookup에 `[ReadOnly]` / `isReadOnly: true` 누락 (Job Safety 충돌 + 병렬화 차단) |
| 중복 데이터 조회 | 동일 컴포넌트를 Query 결과 + Lookup 양쪽에서 중복 접근 |
| Random Access 패턴 | 정렬되지 않은 엔티티 배열에 대한 대량 Lookup (캐시 비친화적. Chunk iteration이나 SpatialMap 셀 단위 접근으로 대체 가능 시 위반) |

## F-4. System 실행 제어

| 검토 항목 | 위반 조건 |
|-----------|-----------|
| RequireForUpdate 누락 | 전제 싱글톤(GridSettings, GamePhaseState, SpatialMaps 등) 없이 실행되어 NullRef 가능한 시스템에 `RequireForUpdate` 누락 |
| 과도한 RequireForUpdate | 선택적 엔티티(SelectedEntity 등)에 RequireForUpdate 적용하여 시스템이 불필요하게 비활성화 |
| 무조건 매 프레임 실행 | 이벤트/조건 기반으로 충분한 로직이 매 프레임 실행 (early return 조건 추가 가능 시 위반) |

## F-5. NativeContainer 효율

| 검토 항목 | 위반 조건 |
|-----------|-----------|
| 초기 Capacity 부적절 | 예상 엔티티 수 대비 NativeContainer 초기 Capacity가 과소(빈번한 리사이즈) 또는 과대(메모리 낭비) |
| ParallelWriter 미사용 | `ScheduleParallel` Job에서 NativeContainer에 쓰기 시 `.AsParallelWriter()` 미사용 (race condition) |
| Clear + 재사용 미적용 | 매 프레임 동일 구조의 Persistent 컨테이너를 Dispose + 재할당 (Clear로 재사용해야 함. SpatialMapBuildSystem 패턴 참조) |
| HashMap vs MultiHashMap | 키당 다수 값이 필요한데 HashMap + 충돌 처리로 구현 (NativeParallelMultiHashMap 사용해야 함) |
