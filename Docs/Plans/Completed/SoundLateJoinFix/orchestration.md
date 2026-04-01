# 사운드 Ghost Relevancy 버그 수정 오케스트레이션 플랜

## 문제 정의
멀티플레이 환경에서 **Ghost Relevancy에 의한 Ghost 재생성** 시 과거 사운드가 재발생하는 버그.

`GhostRelevancySystem`은 카메라 뷰포트 밖 엔티티(적, 다른 유저 유닛)를 irrelevant 처리 → Ghost 파괴. 카메라가 해당 영역으로 돌아오면 Ghost 재생성 → `PreviousActionState`/`PreviousEnemyContext` 미부착 → 사운드 시스템이 "신규 엔티티"로 감지.

1. **UnitSpawn 사운드 재발생**: 다른 유저 유닛이 뷰포트 재진입 시 `UnitSpawn` 사운드 발생
2. **공격 사운드 즉시 재생**: 이미 Attacking 상태인 유닛/적의 `AttackSoundTimer` 기본값 0 → 즉시 공격 사운드 발생

- **영향 범위**: `SoundEventEmitSystem` (Client) 1개 파일
- **근본 원인**: 자기 유닛은 항상 relevant (GhostRelevancySystem에서 skip), 적/다른 유저 유닛만 AABB 필터링 대상

## AS-IS (현재 상태)

### 관련 파일
| 파일 | 역할 |
|---|---|
| `Assets/Scripts/Client/Systems/Sound/SoundEventEmitSystem.cs` | 사운드 이벤트 발행 시스템 |
| `Assets/Scripts/Client/Component/State/PreviousActionState.cs` | 유닛 이전 상태 추적 |
| `Assets/Scripts/Client/Component/State/PreviousEnemyContext.cs` | 적 이전 상태 추적 |
| `Assets/Scripts/Server/Systems/Network/GhostRelevancySystem.cs` | Ghost Relevancy AABB 필터링 (참조만) |

### 현재 동작

**GhostRelevancySystem** (Server):
- `SetIsIrrelevant` 모드, OuterHalf x 1.3 밖 → irrelevant, InnerHalf x 1.15 안 → relevant
- 자기 유닛 (`ownerId == conn.NetworkIdValue`) → skip (항상 relevant)
- 적 (`ownerId = -1`) / 다른 유저 유닛 → AABB 필터링 대상

**InitializePreviousStates** (line 141-171):
- `UnitActionState` + `WithNone<PreviousActionState>` 쿼리로 신규 엔티티 감지
- 무조건 `spawnPositions`에 위치 추가 → `UnitSpawn` 사운드 발행
- "실제 스폰" vs "Ghost 재생성" 구분 없음
- `AttackSoundTimer`는 struct 기본값 0으로 초기화

**OnUpdate 3-4단계** (line 59-138):
- `current == previous && current == Attacking` 조건에서 `AttackSoundTimer -= deltaTime`
- 타이머 <= 0이면 공격 사운드 발행
- Ghost 재생성 직후 타이머 0 → 즉시 발동

### 버그 재현 시나리오
1. 적/다른 유저 유닛이 전투 중
2. 카메라를 다른 영역으로 이동 → 뷰포트 밖 엔티티 irrelevant → Ghost 파괴
3. 카메라를 다시 해당 영역으로 이동 → Ghost 재생성
4. 다른 유저 유닛: `PreviousActionState` 없음 → `UnitSpawn` + 즉시 공격 사운드
5. 적: `PreviousEnemyContext` 없음 → 즉시 공격 사운드

## TO-BE (목표 상태)

### 변경 사항
1. **`GhostOwnerIsLocal` 필터링**: 스폰 사운드를 자기 유닛에만 한정. 자기 유닛은 항상 relevant이므로 Ghost 재생성 문제 없음.
2. **`AttackSoundTimer` 초기값 설정**: 이미 Attacking 상태인 엔티티는 타이머를 공격 간격으로 초기화하여 Ghost 재생성 시 즉시 발동 방지.

### 목표 동작
- 자기 유닛 스폰: `UnitSpawn` 사운드 정상 발생
- 다른 유저 유닛/적 Ghost 재생성: 스폰 사운드 없음, 공격 사운드는 타이머 경과 후 자연스럽게 발생
- 상태 전이 사운드: 정상 (변경 없음)

## AS-IS vs TO-BE 비교표
| 항목 | AS-IS | TO-BE |
|---|---|---|
| 스폰 사운드 대상 | 모든 UnitActionState 엔티티 | GhostOwnerIsLocal 엔티티만 |
| Ghost 재생성 시 스폰 사운드 | 발생 | 미발생 (자기 유닛은 재생성 없음) |
| Attacking 엔티티 초기화 시 타이머 | 0 (즉시 발동) | 공격 간격 (1회 대기 후 발동) |
| 상태 전이 사운드 | 정상 | 정상 (변경 없음) |

## Phase 체크리스트

### Phase 1: GhostOwnerIsLocal 필터링 + 타이머 초기화
- [x] `ghostOwnerIsLocalLookup` 추가 및 스폰 사운드 조건 분기
- [x] `combatStatsLookup.Update` 호출 순서 조정
- [x] `AttackSoundTimer` 초기값을 공격 간격으로 설정
- [x] 주석 업데이트
- [x] 컴파일 확인
-> 상세: [phase-1-GhostOwnerIsLocal-필터링.md](./phase-1-GhostOwnerIsLocal-필터링.md)

## Phase 간 의존성
| Phase | 의존성 | 병렬 가능 |
|---|---|---|
| 1 | 없음 | - |

## 변경 파일 요약
| Phase | 파일 | 변경 |
|---|---|---|
| 1 | `Assets/Scripts/Client/Systems/Sound/SoundEventEmitSystem.cs` | `ghostOwnerIsLocalLookup` 추가, 스폰 사운드 필터링, Lookup 순서 조정, 타이머 초기값 |

## 검증 방법
1. 컴파일 성공 확인
2. 멀티플레이 테스트: 카메라를 전투 영역 밖으로 이동 후 복귀 → 스폰/공격 사운드 재발생 없음 확인
3. 정상 스폰 테스트: 자기 유닛 스폰 시 `UnitSpawn` 사운드 정상 발생 확인
4. 정상 전투 테스트: 상태 전이 사운드(Attacking, Dying 등) 정상 발생 확인

## 롤백 전략
- 단일 파일 변경이므로 `git checkout -- SoundEventEmitSystem.cs`로 즉시 롤백 가능
