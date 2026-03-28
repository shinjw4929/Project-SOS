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
| 성능 | Structural Change를 루프 내에서 반복, 불필요한 할당, O(n²) 탐색 (Spatial Map 사용 가능 시) |
| 엣지 케이스 | 엔티티 파괴, 연결 끊김, null Entity 미처리 |
