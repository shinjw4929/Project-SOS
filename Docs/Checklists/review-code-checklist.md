# 코드 리뷰 체크리스트

> `/review-code`, `/review-plan` 스킬이 참조하는 상세 검토 항목.
> 규칙의 근거와 원칙은 `CLAUDE.md` Development Guidelines에 정의되어 있다.

## A. DOTS 규칙

| 검토 항목 | 위반 조건 |
|-----------|-----------|
| Burst 컴파일 | 새 시스템/Job에 `[BurstCompile]` 누락 (입력/managed 제외) |
| Job System | 연산 로직이 메인 스레드에서 실행 (IJobEntity 미사용) |
| DamageEvent 패턴 | Health를 직접 수정 (`health.CurrentValue` 등) |
| ECB 사용 | 엔티티 생성/파괴/컴포넌트 추가·제거를 ECB 없이 수행 |
| Safe Lookup | `ComponentLookup[entity]` 직접 접근 (TryGetComponent 미사용) |
| 권한 최소화 | `RefRW<T>` 사용 시 `RefRO<T>`로 충분한 경우 |
| Tag 컴포넌트 | bool 필드로 상태 구분 (Tag + Query 필터링으로 대체 가능) |
| bool blittable | Burst struct에 `bool` 필드 + `ref` 전달 시 `[MarshalAs(UnmanagedType.U1)]` 누락 |
| BurstCompile static | `[BurstCompile]` static 메서드에서 struct 파라미터/반환 사용 (BC1064) |
| Entity 안전성 | `Entity.Null` 비교 없이 Entity 사용, `EntityManager.Exists()` 미검증, `.IsCreated` 미확인 |

## B. 네트워크 아키텍처

| 검토 항목 | 위반 조건 |
|-----------|-----------|
| Server Authority | 게임 로직이 Client 어셈블리에서 실행 |
| Ghost 동기화 | 네트워크 동기화 필요한 컴포넌트에 `[GhostField]` 누락 |
| RPC 방향 | Client→Server 요청이 아닌 방향으로 게임 로직 RPC 전송 |
| Client/Server 분리 | Server 전용 로직이 Shared에 위치하거나 그 반대 |

## C. 프로젝트 컨벤션

| 검토 항목 | 위반 조건 |
|-----------|-----------|
| 네이밍 - User | "Player" 사용 (Unity Player와 혼동, "User" 사용해야 함) |
| 네이밍 - 변수명 | 의미 불명의 축약 변수명 (`var c`, `var t` 등). 단, `ecb`, `em`, `job` 등 DOTS 관용어는 허용 |
| 네이밍 - 파일명 | 폴더별 네이밍 패턴 불일치 (Commands → `*InputSystem.cs`, RPCs → `*Rpc.cs` 등) |
| GameSettings 패턴 | 밸런스/규칙 상수를 시스템 코드에 직접 작성 (GameSettings 미사용). Job 내부에서 GameSettings를 직접 읽는 경우도 위반 (OnUpdate에서 읽어 구조체 필드로 전달해야 함) |
| 중복 구현 | 기존 유틸리티(ArrivalUtility, CombatUtility, SpatialMaps 등)를 재구현 |
| Work Range 패턴 | 작업 거리를 인라인 계산. `ArrivalUtility.GetInteractionArrivalDistance`/`CombatUtility` 사용해야 함 |
| 싱글톤 중복 | 기존 싱글톤을 확장할 수 있는데 새 싱글톤 생성 |
| Authoring 패턴 | Authoring 조합 불일치 (유닛: `Movement`+`UnitMovement`+`Unit`, 적: `Movement`+`Enemy`, 건물: `Structure`) |
| 기존 패턴 일관성 | 같은 유형의 기존 코드(같은 SystemGroup 내 시스템, 같은 폴더 내 컴포넌트 등)와 구조(어트리뷰트 배치, 필드 순서, 메서드 구성, ECB/Query 패턴)가 일치하지 않는 경우 |

## D. 시스템 설계

| 검토 항목 | 위반 조건 |
|-----------|-----------|
| SystemGroup 배치 | 새 시스템의 Group 배치가 실행 순서상 부적절 |
| 의존성 선언 | 필요한 `UpdateAfter`/`UpdateBefore` 누락으로 데이터 레이스 가능 |
| CompleteDependency | 같은 SystemGroup 내에서 `CompleteDependency` 사용 (UpdateAfter로 대체 가능) |
| Job 스케줄링 충돌 | 동일 컴포넌트에 대한 ReadWrite 접근이 다른 시스템과 충돌 가능 |

## E. 코드 품질

| 검토 항목 | 위반 조건 |
|-----------|-----------|
| 보안 | 커맨드 인젝션, 검증 없는 외부 입력 처리 |
| Structural Change | Structural Change를 루프/Job 내에서 반복 (ECB로 지연 처리해야 함) |
| 엣지 케이스 | 엔티티 파괴, 연결 끊김, null Entity 미처리 |

## F. 성능

### F-1. 메모리 할당

| 검토 항목 | 위반 조건 |
|-----------|-----------|
| Allocator 선택 | Job에 전달하는 NativeContainer에 `Allocator.Temp` 사용 (`TempJob` 사용해야 함). 프레임 범위 로컬 변수에 `TempJob`/`Persistent` 사용 |
| TempJob Dispose | `Allocator.TempJob` 할당 후 명시적 `Dispose()` 누락 (메모리 누수) |
| Persistent Dispose | `Allocator.Persistent` 할당 후 `OnDestroy`에서 `.IsCreated` 체크 + `Dispose()` 누락 |
| 매 프레임 할당 | `ToComponentDataArray`, `ToEntityArray` 등 Query 복사를 매 프레임 무조건 호출 (조건부 실행 가능 시 위반. 예: `ProductionProgressSystem`의 `anyCompletingThisFrame` 패턴 참조) |
| 불필요한 할당 | 결과를 사용하지 않는 NativeContainer 할당, 또는 기존 Persistent 컨테이너로 대체 가능한 Temp 재할당 |

### F-2. Job 스케줄링

| 검토 항목 | 위반 조건 |
|-----------|-----------|
| 불필요한 CompleteDependency | 같은 SystemGroup 내에서 `CompleteDependency`/`Dependency.Complete()` 호출 (`UpdateAfter`로 순서 보장 가능 시 위반) |
| 병렬화 미활용 | 독립적인 IJobEntity를 순차 스케줄링 (동일 컴포넌트 ReadWrite 충돌이 없으면 `ScheduleParallel` + `JobHandle.CombineDependencies`로 병렬화 가능) |
| Schedule vs Run | 엔티티 수가 충분한 IJobEntity에 `Run()` 사용 (`ScheduleParallel` 사용해야 함). 반대로 엔티티 1~2개 보장 시스템에 `ScheduleParallel` 오버헤드 |
| 의존성 체인 누락 | Job 핸들을 `state.Dependency`에 연결하지 않아 다음 시스템과 데이터 레이스 발생 |

### F-3. Lookup / 데이터 접근

| 검토 항목 | 위반 조건 |
|-----------|-----------|
| O(n²) 탐색 | 전체 엔티티 순회로 최근접/범위 탐색 수행 (SpatialMaps 사용 가능 시 위반. TargetingMap=10f, MovementMap=3f) |
| Lookup 과다 | 단일 시스템/Job에 Lookup 10개 이상 보유 시 분리 가능성 검토 (캐시 미스 증가) |
| Lookup ReadOnly 미적용 | 읽기 전용 Lookup에 `[ReadOnly]` / `isReadOnly: true` 누락 (Job Safety 충돌 + 병렬화 차단) |
| 중복 데이터 조회 | 동일 컴포넌트를 Query 결과 + Lookup 양쪽에서 중복 접근 |
| Random Access 패턴 | 정렬되지 않은 엔티티 배열에 대한 대량 Lookup (캐시 비친화적. Chunk iteration이나 SpatialMap 셀 단위 접근으로 대체 가능 시 위반) |

### F-4. System 실행 제어

| 검토 항목 | 위반 조건 |
|-----------|-----------|
| RequireForUpdate 누락 | 전제 싱글톤(GridSettings, GamePhaseState, SpatialMaps 등) 없이 실행되어 NullRef 가능한 시스템에 `RequireForUpdate` 누락 |
| 과도한 RequireForUpdate | 선택적 엔티티(SelectedEntity 등)에 RequireForUpdate 적용하여 시스템이 불필요하게 비활성화 |
| 무조건 매 프레임 실행 | 이벤트/조건 기반으로 충분한 로직이 매 프레임 실행 (early return 조건 추가 가능 시 위반) |

### F-5. NativeContainer 효율

| 검토 항목 | 위반 조건 |
|-----------|-----------|
| 초기 Capacity 부적절 | 예상 엔티티 수 대비 NativeContainer 초기 Capacity가 과소(빈번한 리사이즈) 또는 과대(메모리 낭비) |
| ParallelWriter 미사용 | `ScheduleParallel` Job에서 NativeContainer에 쓰기 시 `.AsParallelWriter()` 미사용 (race condition) |
| Clear + 재사용 미적용 | 매 프레임 동일 구조의 Persistent 컨테이너를 Dispose + 재할당 (Clear로 재사용해야 함. SpatialMapBuildSystem 패턴 참조) |
| HashMap vs MultiHashMap | 키당 다수 값이 필요한데 HashMap + 충돌 처리로 구현 (NativeParallelMultiHashMap 사용해야 함) |
