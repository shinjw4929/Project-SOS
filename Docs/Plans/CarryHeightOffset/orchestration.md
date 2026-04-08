# CarryHeightOffset 오케스트레이션 플랜

## 문제 정의
- `CarriedResourceFollowSystem`에서 자원(Cheese)의 운반 높이가 `Y + 1.2f`로 하드코딩되어 있음
- Worker(콜라이더 높이 1.4)와 Hero(콜라이더 높이 2.0)처럼 키가 다른 유닛이 동일한 오프셋을 사용하므로 시각적으로 부자연스러움
- 향후 다른 크기의 유닛이 추가되어도 대응 불가

## AS-IS (현재 상태)

### 관련 파일
| 파일 | 역할 |
|---|---|
| `Assets/Scripts/Shared/Systems/CarriedResourceFollowSystem.cs` | 자원 위치를 Worker 위치 + 고정 오프셋으로 설정 |
| `Assets/Scripts/Server/Systems/Gathering/WorkerCarriedResourceSpawnSystem.cs` | Worker/Hero 생성 시 CarriedResource 엔티티 자동 생성 |
| `Assets/Scripts/Shared/Components/State/CarriedResourceOwner.cs` | CarriedResource → Worker 연결 (Ghost 동기화) |
| `Assets/Scripts/Authoring/Entities/UnitAuthoring.cs` | 유닛 프리팹 Authoring (Worker/Hero 포함) |

### 현재 동작
1. `WorkerCarriedResourceSpawnSystem`이 `WorkerTag` 보유 엔티티(Worker, Hero)에 CarriedResource 생성
2. `CarriedResourceFollowSystem`이 매 프레임 Worker 위치 + `new float3(0, 1.2f, 0)` 적용
3. 유닛 높이와 무관하게 동일한 1.2f 오프셋 사용

### 유닛별 콜라이더 높이
| 유닛 | 콜라이더 높이 | 콜라이더 중심 Y | 머리 꼭대기 |
|---|---|---|---|
| Worker | 1.4 | 0.7 | 1.4 |
| Hero | 2.0 | 1.0 | 2.0 |

## TO-BE (목표 상태)

### 변경 사항
1. **`CarryHeightOffset` 컴포넌트 추가**: 유닛별 자원 운반 높이 오프셋을 per-entity로 설정
2. **`UnitAuthoring`에 필드 추가**: `carryHeightOffset` 인스펙터 필드, `isWorker` 분기에서 베이킹
3. **`CarriedResourceFollowSystem` 수정**: 하드코딩 1.2f 대신 Worker 엔티티의 `CarryHeightOffset.Value` 참조

### 변경 후 동작
1. Worker 프리팹: `carryHeightOffset = 1.2f` (기존과 동일)
2. Hero 프리팹: `carryHeightOffset = 1.8f` (키에 맞게 조정)
3. `CarriedResourceFollowSystem`이 Worker 엔티티에서 `CarryHeightOffset`을 읽어 동적 오프셋 적용
4. 오프셋이 없는 엔티티는 기존 1.2f를 fallback으로 사용

### 설계 판단
- **GameSettings 대신 per-entity 컴포넌트**: 유닛별로 다른 시각적 속성이므로 싱글톤보다 per-entity가 적합
- **Ghost 동기화 불필요**: 프리팹 베이킹 시 결정되는 상수값으로, 양쪽(서버/클라이언트) 모두 동일 프리팹에서 베이킹됨
- **기존 패턴 준수**: `GatheringAbility`와 동일한 구조 (`namespace Shared`, Ghost 동기화 없는 Worker 전용 Stats 컴포넌트)

## AS-IS vs TO-BE 비교표
| 항목 | AS-IS | TO-BE |
|---|---|---|
| 높이 오프셋 소스 | 하드코딩 `1.2f` | `CarryHeightOffset.Value` (per-entity) |
| 유닛별 차별화 | 불가 | 프리팹 인스펙터에서 개별 설정 |
| 새 유닛 추가 시 | 코드 수정 필요 | Authoring 인스펙터에서 설정만 |
| 컴포넌트 수 | 변화 없음 | `CarryHeightOffset` 1개 추가 |
| Ghost 동기화 | - | 불필요 (프리팹 상수) |

## Phase 체크리스트

### Phase 1: CarryHeightOffset 컴포넌트 추가 및 시스템 수정
- [ ] `CarryHeightOffset` IComponentData 생성
- [ ] `UnitAuthoring`에 `carryHeightOffset` 필드 추가 및 베이킹
- [ ] `CarriedResourceFollowSystem`에서 `CarryHeightOffset` Lookup 추가 및 적용
- [ ] Worker/Hero 프리팹 인스펙터에서 값 설정 (Unity Editor 수동 작업)
- [ ] 컴파일 확인
→ 상세: [phase-1-carry-height-component.md](./phase-1-carry-height-component.md)

## Phase 간 의존성
| Phase | 의존성 | 병렬 가능 |
|---|---|---|
| 1 | 없음 | - |

## 변경 파일 요약
| Phase | 파일 | 변경 |
|---|---|---|
| 1 | `Assets/Scripts/Shared/Components/Stats/CarryHeightOffset.cs` | 신규 생성 |
| 1 | `Assets/Scripts/Authoring/Entities/UnitAuthoring.cs` | `carryHeightOffset` 필드 + 베이킹 추가 |
| 1 | `Assets/Scripts/Shared/Systems/CarriedResourceFollowSystem.cs` | Lookup 추가, 하드코딩 → 컴포넌트 값 참조 |
| 1 | `Docs/Systems/자원 채집 시스템.md` | `Y+1.2f` 설명을 CarryHeightOffset 기반으로 갱신 |
| 1 | `Docs/Systems/코드베이스 구조.md` | Stats 컴포넌트 목록에 `CarryHeightOffset.cs` 추가 |

## 검증 방법
1. 컴파일 성공 확인
2. Unity Editor에서 Worker/Hero 프리팹에 `carryHeightOffset` 값 설정
3. 플레이 테스트: Worker/Hero가 자원 운반 시 각각 다른 높이에 cheese 표시 확인

## 롤백 전략
- Phase 1 실패 시: `CarryHeightOffset.cs` 삭제, `UnitAuthoring.cs`와 `CarriedResourceFollowSystem.cs` 변경 되돌리기 (git checkout)
