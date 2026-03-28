# Phase 5: Context Firewall + Hooks

## 목표
- review-code에 서브에이전트 전략을 적용하여 컨텍스트 오염 방지
- hooks를 설정하여 자동 피드백 루프 구성

## 선행 조건
- Phase 2 완료 (review-code 경량화 완료 상태)
- Phase 3 완료 (settings.local.json 정리 완료 상태 - hooks 추가 대상)
- Phase 4 완료 (/build 스킬 존재)

## 작업 목록

### Task 1: review-code 서브에이전트 전략
- [ ] review-code SKILL.md의 3단계를 서브에이전트 위임 구조로 변경:
  ```
  3단계: 파일별 검토 (서브에이전트)

  변경 파일이 3개 이상인 경우 Agent 도구로 병렬 검토:
  - 각 Agent에게 파일 경로 + 변경 diff + 체크리스트 경로를 전달
  - Agent는 해당 파일만 읽고 체크리스트 기준으로 검토
  - 결과를 `파일:라인 | 심각도 | 항목 | 설명` 형식으로 반환

  변경 파일이 2개 이하인 경우 직접 검토 (서브에이전트 오버헤드 불필요).
  ```
- [ ] `allowed-tools`에 `Agent` 추가
- [ ] 4단계(결과 취합)는 메인 에이전트가 담당:
  - 서브에이전트 결과를 통합
  - 파일 간 의존성/정합성 문제 추가 검토 (서브에이전트는 개별 파일만 봄)
  - 최종 리뷰 테이블 생성

### Task 2: Hooks 설계 및 설정
- [ ] hooks 설정을 settings.local.json에 추가
- [ ] 적용할 hooks:

**Hook 1: 스킬 완료 알림 (선택적)**
```jsonc
// 장시간 스킬(plan-execute 등) 완료 시 알림
// Windows에서는 PowerShell toast 또는 beep
```

**Hook 2: commit 전 검증 (핵심)**
```jsonc
// /commit 스킬 호출 시 자동으로 빌드 오류가 없는지 확인
// Unity CLI 빌드가 느리므로 경량 검증 고려:
// - .cs 파일 문법 오류: dotnet build 활용 (DOTS 의존성 한계 있음)
// - 대안: 마지막 빌드 이후 변경된 .cs 파일 목록만 출력 (수동 판단 지원)
```

- [ ] Unity 프로젝트의 빌드 속도 제약을 고려하여 hooks의 실행 조건을 보수적으로 설정
- [ ] hooks 오류 시 에이전트 작업을 차단하지 않도록 `timeout` 설정

### Task 3: Hooks와 기존 스킬 연동 확인
- [ ] commit 스킬에서 hooks가 정상 트리거되는지 확인
- [ ] **commit 스킬의 `disable-model-invocation: true` 모드에서 hook 동작 검증** (템플릿 실행 모드에서도 tool call hooks가 정상 발화하는지)
- [ ] hooks 실패 시 에이전트가 적절히 대응하는지 확인 (중단 vs 경고)
- [ ] plan-execute의 Phase 완료 시점에서 /build 호출 안내 추가

## 병렬 작업 구성

| Agent | 작업 내용 | 의존성 |
|-------|----------|--------|
| Agent A | Task 1 (review-code 서브에이전트) | 없음 |
| Agent B | Task 2 (hooks 설계) | 없음 |
| - | Task 3 (연동 확인) | Task 1, 2 완료 후 |

## 테스트 요구사항

### 수동 검증
- `/review-code` 호출 시 파일 3개 이상 변경된 상황에서 서브에이전트가 병렬 실행되는지 확인
- 서브에이전트 결과가 메인에 정상 취합되는지 확인
- hooks 트리거 조건이 정상 작동하는지 확인
- hooks timeout 시 에이전트 작업이 중단되지 않는지 확인

## 검증 방법
- review-code의 `allowed-tools`에 `Agent` 포함 확인
- settings.local.json에 `hooks` 섹션 존재 확인
- 서브에이전트 검토 결과가 누락 없이 취합되는지 확인 (원본 diff 대조)

## 완료 기준
- [ ] review-code에 서브에이전트 전략 반영됨
- [ ] 파일 3개 이상 시 병렬 검토, 2개 이하 시 직접 검토 분기 동작
- [ ] settings.local.json에 hooks 설정 추가됨
- [ ] hooks 트리거 + timeout 정상 작동 확인
- [ ] plan-execute에 Phase 완료 시 /build 호출 안내 추가됨
