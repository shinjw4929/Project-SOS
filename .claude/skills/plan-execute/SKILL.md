---
name: plan-execute
description: Docs/Plans/ 아래의 구현 계획(오케스트레이션)을 읽고 Phase별로 순차 실행하며, 실행 기록을 자동 갱신합니다.
allowed-tools: Read, Edit, Write, Grep, Glob, Bash, Agent, EnterPlanMode, ExitPlanMode
---

## 역할

기존에 생성된 구현 계획(오케스트레이션 파일)을 읽고, Phase를 순차적으로 실행한다.
각 Phase 완료 시 `execution-log.md`를 자동 갱신한다.

$ARGUMENTS로 오케스트레이션 파일 경로 또는 기능명을 전달받는다.

## 실행 절차

### 0단계: 오케스트레이션 로드

1. $ARGUMENTS에서 경로를 추출한다.
   - 경로가 주어지면 해당 파일을 읽는다.
   - 기능명만 주어지면 `Docs/Plans/[기능명]/orchestration.md`를 찾는다.
   - 없으면 `Docs/Plans/*/orchestration.md`를 Glob으로 탐색하여 목록을 보여주고 사용자에게 선택을 요청한다.
2. 오케스트레이션 파일에서 Phase 체크리스트와 의존성을 파악한다.
3. `execution-log.md`를 읽어 이미 완료된 Phase를 확인한다. 완료된 Phase는 건너뛴다.

### 1단계: 다음 Phase 결정

1. 완료되지 않은 Phase 중 선행 조건이 충족된 가장 빠른 Phase를 선택한다.
2. 해당 Phase 파일을 읽어 작업 목록, 테스트 요구사항, 완료 기준을 확인한다.
3. 사용자에게 실행할 Phase의 요약을 보여주고 진행 확인을 받는다.

### 2단계: Phase 실행

Phase 파일의 작업 목록을 순서대로 수행한다:

1. **Docs 참조**: 작업 대상 시스템의 관련 문서를 먼저 읽는다.
2. **병렬 작업**: Phase 파일에 subagent 병렬 구성이 명시된 경우, Agent 도구로 독립적인 작업을 병렬 실행한다. 같은 파일을 수정하는 작업은 병렬로 실행하지 않는다.
3. **순차 작업**: 의존성이 있는 작업은 순서대로 실행한다.
4. **DOTS 규칙 준수**: 구현 시 CLAUDE.md의 Development Guidelines를 따른다.
5. **GameSettings 패턴**: 새 밸런스/규칙 상수는 GameSettings 싱글톤에 추가한다.

### 3단계: Phase 검증

Phase 파일의 검증 방법과 완료 기준을 확인한다:

1. 컴파일 확인 (해당 시)
2. 테스트 실행 (Phase 파일에 명시된 테스트)
3. 완료 기준 체크리스트 점검

검증 실패 시:
- 실패 원인을 분석하고 수정한다.
- 수정 후 재검증한다.
- 반복 실패 시 사용자에게 보고하고 판단을 요청한다.

### 4단계: 기록 갱신

Phase 완료 후 다음을 수행한다:

**A. orchestration.md 체크박스 갱신:**
- 완료된 작업 항목을 `[x]`로 체크한다.

**B. execution-log.md 갱신:**

```markdown
## Phase N: [제목] - [날짜]

### 실행 내역
| 작업 | 결과 | 비고 |
|---|---|---|
| ... | ... | ... |

### 변경된 파일
- `path/to/file.cs` - 변경 내용 요약

### 발견된 이슈
- [이슈] → [대응]

### Phase N 완료 판정: Pass / Fail
```

**C. Phase 파일 체크박스 갱신:**
- 완료 기준 체크박스를 모두 체크한다.

### 5단계: 후속 처리

1. 다음 Phase가 남아있으면 1단계로 돌아간다.
2. 모든 Phase가 완료되면 사용자에게 최종 보고한다:
   - 전체 변경 파일 수
   - 주요 변경 사항 요약
   - `/review-code`로 최종 코드 리뷰를 권장
   - `/update-docs`로 Docs/Systems/ 진리 문서 동기화를 권장
   - `/commit`으로 커밋을 권장
   - 계획 폴더를 `Docs/Plans/Completed/`로 이동할지 확인

## 주의사항

- **Phase 순서 엄수**: 선행 조건이 충족되지 않은 Phase는 실행하지 않는다.
- **검증 실패 시 중단**: 검증을 통과하지 못한 Phase가 있으면 다음 Phase로 넘어가지 않는다.
- **사용자 확인**: 각 Phase 시작 전 사용자 확인을 받는다. 사용자가 명시적으로 "전체 실행" 또는 "자동 진행"을 요청한 경우에만 확인 없이 연속 실행한다.
- **실행 기록 즉시 갱신**: Phase 완료 즉시 execution-log.md를 갱신한다. 여러 Phase를 묶어서 나중에 기록하지 않는다.
- **기존 코드 존중**: Phase 파일에 명시되지 않은 리팩토링이나 개선을 임의로 수행하지 않는다.
- **WorkLog 미사용**: 계획 실행 기록은 execution-log.md에 기록한다. WorkLog는 계획 밖 단발 작업 전용이다.
