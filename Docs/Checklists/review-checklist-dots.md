# DOTS / 네트워크 리뷰 체크리스트

> `/review-code` 스킬의 DOTS 에이전트, `/review-plan` 스킬이 참조하는 검토 항목.

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

## D. 시스템 설계

| 검토 항목 | 위반 조건 |
|-----------|-----------|
| SystemGroup 배치 | 새 시스템의 Group 배치가 실행 순서상 부적절 |
| 의존성 선언 | 필요한 `UpdateAfter`/`UpdateBefore` 누락으로 데이터 레이스 가능 |
| CompleteDependency | 같은 SystemGroup 내에서 `CompleteDependency` 사용 (UpdateAfter로 대체 가능) |
| Job 스케줄링 충돌 | 동일 컴포넌트에 대한 ReadWrite 접근이 다른 시스템과 충돌 가능 |
