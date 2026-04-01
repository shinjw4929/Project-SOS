# 실행 기록

## 계획 수정 - 2026-03-31

### 수정 사유
- 실제 버그 원인이 "늦은 합류 초기 동기화"가 아닌 **Ghost Relevancy에 의한 Ghost 재생성**임이 확인됨
- `_initialSyncDone` 첫 프레임 스킵 방식은 카메라 이동 시 반복 발생하는 Ghost 재생성에 대응 불가

### 수정 내역
| 항목 | 변경 전 | 변경 후 |
|---|---|---|
| 문제 정의 | 늦은 합류 시 초기 동기화 | Ghost Relevancy 재진입 시 사운드 재발생 |
| Phase 1 Task 1 | `_initialSyncDone` 첫 프레임 스킵 | `GhostOwnerIsLocal` Lookup으로 자기 유닛만 스폰 사운드 |
| Phase 1 파일명 | phase-1-초기동기화-사운드스킵.md | phase-1-GhostOwnerIsLocal-필터링.md |
| 알려진 한계 | Ghost 스트리밍 2-3프레임 | 삭제 (근본 해결) |

### 영향받는 Phase
- Phase 1: Task 1 전면 교체, Task 2 근거 보강, Task 3 주석 내용 변경

---

## Phase 1: GhostOwnerIsLocal 필터링 + 타이머 초기화 - 2026-04-01

### 실행 내역
| 작업 | 결과 | 비고 |
|---|---|---|
| Task 1: GhostOwnerIsLocal Lookup 추가 + 스폰 사운드 필터링 | Pass | `using Unity.NetCode` 추가, ghostOwnerIsLocalLookup 필드/초기화/Update 추가, HasComponent+IsComponentEnabled 패턴 적용 |
| Task 2: AttackSoundTimer 초기값 설정 | Pass | combatStatsLookup.Update를 InitializePreviousStates 전으로 이동, 유닛/적 모두 Attacking 상태 시 공격 간격으로 초기화 |
| Task 3: 주석 업데이트 | Pass | 클래스 summary + InitializePreviousStates 주석 수정 |

### 변경된 파일
- `Assets/Scripts/Client/Systems/Sound/SoundEventEmitSystem.cs` - GhostOwnerIsLocal 필터링 추가, AttackSoundTimer 초기화 로직, Lookup 순서 조정, 주석 갱신

### 발견된 이슈
- review-plan에서 `using Unity.NetCode` 누락 발견 → phase 파일에 Task 항목 추가하여 반영
- 변수명 충돌 (`var state` vs 파라미터 `ref SystemState state`) → `var currentState`로 수정

### Phase 1 완료 판정: Pass (컴파일 확인 완료, 수동 멀티플레이 테스트 미실행)
