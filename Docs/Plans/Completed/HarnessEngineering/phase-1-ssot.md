# Phase 1: SSOT 확립 - 중복 제거

## 목표
- CLAUDE.md를 규칙의 **유일한 정의 장소**로 확립
- 스킬에서 인라인 규칙을 제거하고 CLAUDE.md 참조로 교체
- 규칙 수정 시 한 곳만 변경하면 되는 구조로 전환

## 선행 조건
- 없음 (첫 번째 Phase)

## 작업 목록

### Task 1: CLAUDE.md 정비
- [ ] `CLAUDE.md` 75~93줄 (커밋 메시지 작성 가이드 섹션) 제거
- [ ] commit 스킬이 커밋 형식의 유일한 정의 장소가 됨을 확인
- [ ] CLAUDE.md의 Pre/Post-Implementation Checklist에서 커밋 관련 언급이 없는지 확인
- [ ] Post-Implementation Checklist의 WorkLog 기록 항목을 선택적으로 변경 (plan-execute는 execution-log 사용, 단발 작업에만 WorkLog)

### Task 2: review-code 스킬 - 인라인 규칙 → CLAUDE.md 참조
- [ ] 3단계 검토 항목의 5개 테이블(A~E)을 다음 패턴으로 교체:
  ```
  CLAUDE.md의 Development Guidelines를 기준으로 검토한다.
  ```
  > 주의: 상세 체크리스트(Docs/Checklists/) 참조는 Phase 2에서 파일 생성 후 추가한다.
- [ ] 2단계 "Docs 참조" 테이블은 Documentation-Checklist.md 참조로 교체:
  ```
  Docs/Documentation-Checklist.md의 "변경 유형별 업데이트 대상" 테이블을 역으로 활용하여
  변경된 시스템에 대응하는 문서를 읽는다.
  ```
- [ ] 4단계 심각도 기준은 유지 (절차에 해당하므로)

### Task 3: review-plan 스킬 - 인라인 규칙 → CLAUDE.md 참조
- [ ] 2단계 검토 기준의 A~E 항목을 다음 패턴으로 교체:
  ```
  CLAUDE.md의 Development Guidelines를 기준으로 검증한다.
  ```
  > 주의: 상세 체크리스트 참조는 Phase 2에서 추가한다.
- [ ] 개별 DOTS 규칙, GameSettings 언급 제거 (CLAUDE.md에 정의되어 있으므로)

### Task 4: plan-create 스킬 - 인라인 규칙 → 참조
- [ ] 주의사항의 "GameSettings 패턴" 항목을 다음으로 교체:
  ```
  CLAUDE.md의 Development Guidelines를 따른다 (GameSettings 패턴 등).
  ```

### Task 5: plan-execute 스킬 - 인라인 규칙 → 참조
- [ ] 2단계의 "DOTS 규칙 준수", "GameSettings 패턴" 항목을 다음으로 교체:
  ```
  CLAUDE.md의 Development Guidelines를 따른다.
  ```

### Task 6: update-docs 스킬 - 매핑 테이블 → 참조
- [ ] 2단계의 인라인 매핑 테이블을 다음으로 교체:
  ```
  Docs/Documentation-Checklist.md의 "변경 유형별 업데이트 대상" 테이블을 따른다.
  ```
- [ ] 4단계의 작성 원칙도 Documentation-Checklist.md의 "문서 작성 원칙" 참조로 교체

## 병렬 작업 구성

| Agent | 작업 내용 | 의존성 |
|-------|----------|--------|
| Agent A | Task 1 (CLAUDE.md) | 없음 |
| Agent B | Task 2 + 3 (review-code + review-plan) | 없음 |
| Agent C | Task 4 + 5 + 6 (plan-create + plan-execute + update-docs) | 없음 |

Task 2~6은 서로 다른 파일이므로 병렬 가능. 단, 같은 파일을 수정하는 Task 2+3과 Task 4+5+6은 각각 하나의 Agent에 묶는다.

## 테스트 요구사항

### 수동 검증
- 변경 후 `/review-code`, `/review-plan` 등을 호출하여 CLAUDE.md와 체크리스트를 정상 참조하는지 확인
- 규칙 변경이 발생했을 때 CLAUDE.md만 수정하면 되는지 시뮬레이션

## 검증 방법
- 모든 스킬 파일에서 DOTS 규칙(BurstCompile, ECB, RefRO 등)을 인라인으로 기술하는 곳이 없는지 Grep으로 확인
- CLAUDE.md의 Development Guidelines가 온전한지 확인
- commit 스킬에 커밋 형식이 완전히 정의되어 있는지 확인

## 완료 기준
- [ ] CLAUDE.md에서 커밋 메시지 섹션 제거됨
- [ ] review-code 스킬에서 인라인 검토 테이블 제거됨 (참조로 교체)
- [ ] review-plan 스킬에서 인라인 검토 기준 제거됨 (참조로 교체)
- [ ] plan-create, plan-execute, update-docs에서 인라인 규칙 제거됨
- [ ] Grep 확인: 스킬 파일에 "BurstCompile", "GameSettings", "DamageEvent" 등 인라인 규칙 정의 없음
- [ ] CLAUDE.md Development Guidelines 섹션 무결성 확인
