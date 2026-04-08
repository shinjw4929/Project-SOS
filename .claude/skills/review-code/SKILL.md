---
name: review-code
description: 코드 변경사항을 3개 도메인 에이전트(DOTS/컨벤션/성능)가 병렬로 리뷰합니다. 사용자가 코드 리뷰, 변경점 검토, 또는 /review-code를 요청할 때 실행합니다.
allowed-tools: Read, Grep, Glob, Bash, Agent, AskUserQuestion
---

## 코드 리뷰 실행

$ARGUMENTS가 있으면 해당 내용을 리뷰 범위/관점으로 반영한다. 유효한 인자 형식: 파일 경로(`Assets/Scripts/...`), 커밋 범위(`HEAD~3..HEAD`), 관점 키워드(`성능 중심`, `네트워크 집중` 등). 인자가 없으면 현재 워킹 트리의 전체 `.cs` 변경을 대상으로 한다.

### 1단계: 변경사항 수집

**인자에 따른 분기:**
- **인자 없음 / 관점 키워드만**: `git diff HEAD --name-only`, `git diff HEAD`, `git status` 병렬 실행
- **파일 경로**: 해당 파일만 `git diff HEAD -- <파일>` + `git status`
- **커밋 범위** (`A..B`, `HEAD~N..HEAD` 등): `git diff <범위> --name-only`, `git diff <범위>` 사용 (`git diff HEAD` 대체)

공통: `git status`는 `-uall` 플래그 사용 금지. `git diff HEAD`는 staged+unstaged를 통합하여 수집한다 (별도 구분 없이 전체 변경을 리뷰).

변경된 `.cs` 파일만 리뷰 대상으로 삼는다. `git status` 출력에서 untracked `.cs` 파일을 식별하고 Read로 전체 내용을 수집한다. 문서, 메타, 설정 파일은 제외. **변경된 `.cs` 파일이 없으면 "리뷰 대상 없음"을 출력하고 종료한다.** 커밋 직후라면 "최근 커밋을 리뷰하려면 `HEAD~1..HEAD` 범위를 지정하세요"를 안내한다.

### 2단계: 체크리스트 로드

다음 파일을 병렬 Read (에이전트 프롬프트에 직접 포함):

| 문서 | 전달 대상 에이전트 |
|------|---------------------|
| `Docs/Checklists/review-checklist-dots.md` | DOTS |
| `Docs/Checklists/review-checklist-convention.md` | 컨벤션 |
| `Docs/Checklists/review-checklist-perf.md` | 성능 |
| `Docs/Checklists/pattern-search-guide.md` | 컨벤션 |

### 3단계: 3개 도메인 에이전트 병렬 실행

**반드시 3개 Agent를 단일 메시지에서 동시 호출.** 각 Agent: `isolation: "worktree"`, `subagent_type: "general-purpose"`.
- worktree 격리 목적: 메인 워킹 트리 보호 + 일관된 코드 스냅샷 보장

각 프롬프트에 변경 파일 목록 + diff 전문 + 해당 체크리스트 전문 + 아래 심각도 기준을 포함. 공통 지시:
- 변경/신규 코드만 리뷰. 기존 코드 문제 지적 금지.
- 각 파일과 주변 코드를 Read하여 맥락 파악.
- 체크리스트 항목을 diff에 대입하여 위반 판정. 추측 금지.
- 심각도 기준:
  - **치명**: 런타임 크래시, 데이터 레이스, 네트워크 비동기, Health 직접 수정, 메모리 누수
  - **경고**: 컨벤션 위반, Burst 누락, 성능 문제, 누락된 의존성 선언, GameSettings 미사용
  - **제안**: 더 나은 대안 존재, 가독성 개선, 사소한 네이밍, 성능 미세 최적화
- 반환 형식 (없으면 `없음`):
  ```
  | # | 도메인 | 심각도 | 파일:라인 | 항목 | 설명 | 제안 |
  ```

**에이전트별 차이:**
- **[DOTS]**: review-checklist-dots.md. 어셈블리 경계, Ghost, ECB, SystemGroup 순서에 주목.
- **[컨벤션]**: review-checklist-convention.md + pattern-search-guide.md. 네이밍, GameSettings, 유틸리티 중복, 기존 패턴 일관성에 주목. pattern-search-guide의 유형별 탐색 전략으로 레퍼런스 코드를 찾아 비교.
- **[성능]**: review-checklist-perf.md. Burst/Job 성능, O(n²), 불필요 할당에 주목.

### 4단계: 결과 병합 및 출력

`없음`인 도메인 제외. 동일 파일:라인 중복은 높은 심각도 유지. 심각도 정렬: 치명 > 경고 > 제안.

```
## 코드 리뷰 결과

### 리뷰 대상
- 변경 파일: N개 (+X / -Y 라인), 에이전트: DOTS / 컨벤션 / 성능

### 문제 발견
| # | 도메인 | 심각도 | 파일:라인 | 항목 | 설명 | 제안 |

### 잘된 점
- (컨벤션을 잘 따른 부분, 좋은 설계 판단)

### 최종 판단
(승인 가능 / 수정 필요)
```

### 5단계: 후속 행동

- **승인 가능**: 종료.
- **수정 필요**: 사용자에게 수정 방법을 안내한다. 개별 항목은 직접 수정, 패턴 불일치가 주요 원인이면 `/implement`로 재구현을 권장. 구현 진행 여부는 AskUserQuestion으로 확인한다. 이 스킬은 코드를 수정하지 않는다.

### 주의사항

- **변경/신규 코드만 리뷰**: 기존 코드 문제 지적 금지 (변경으로 인한 정합성 파괴는 예외). untracked `.cs`는 전체 내용 리뷰.
- **사실 기반**: 코드베이스에서 근거 확인된 항목만 지적. 추측/과잉 지적 금지.
- **효율 우선**: 사소한 스타일보다 런타임 영향 큰 문제에 집중.
- **에이전트 프롬프트에 데이터 직접 포함**: 체크리스트/diff를 프롬프트에 포함하여 추가 Read 최소화.
