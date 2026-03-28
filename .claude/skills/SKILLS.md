# 스킬 사용 시나리오

## 스킬 계층 구조

```
[Tier 1: 오케스트레이션] 사용자 호출 → Tier 2 자동 호출
  implement ──auto──→ /review-comments, /update-docs
  plan-execute ──auto──→ /review-comments, /update-docs
  debug ──auto──→ /review-comments
  plan-create ──auto──→ /review-plan ──auto──→ /plan-edit

[Tier 2: 후처리] 자동 호출 OR 사용자 직접 호출
  review-comments    주석 정합성 점검 및 수정
  update-docs        Docs 문서 동기화

[Tier 2: 검증] 텍스트 권장만 (사용자 직접 호출)
  build              Unity CLI 빌드
  test               EditMode/PlayMode 테스트
  review-code        코드 리뷰

[독립 도구] 사용자 직접 호출
  analyze            의존성/영향도 분석 (읽기 전용)
  commit             커밋 메시지 작성 및 커밋
  plan-edit          계획 부분 수정
  review-plan        계획 검토
```

## 공유 참조 문서

| 문서 | 참조 스킬 |
|------|-----------|
| `Docs/Checklists/pattern-search-guide.md` | implement, plan-execute, debug |
| `Docs/Checklists/review-code-checklist.md` | review-code, review-plan |

---

## 시나리오별 스킬 선택

### 1. 새 기능 구현 (소규모, 단일 시스템/컴포넌트)

```
/implement [구현 대상]
```

패턴 탐색 → 코드 작성 → /review-comments(자동) → /update-docs(자동)
필요 시 사용자가 /build, /test, /review-code 추가 실행.

**예시:**
- "WanderUtility에 새 메서드 추가"
- "EnemyFlying용 새 시스템 생성"
- "GameSettings에 건설 속도 필드 추가"

### 2. 새 기능 구현 (대규모, 여러 시스템에 걸친 변경)

```
/plan-create [기능명]          계획 생성 → /review-plan(자동)
/plan-execute [기능명]         Phase별 실행 → /review-comments(자동) → /update-docs(자동)
```

필요 시 사용자가 /build, /test, /review-code, /commit 추가 실행.

**예시:**
- "자원 채집 시스템 전체 구현"
- "유닛 진형 이동 시스템"
- "적 웨이브 스폰 로직 리워크"

### 3. 기존 계획 수정 후 이어서 실행

```
/plan-edit [기능명]            미실행 Phase 수정
/plan-execute [기능명]         수정된 계획 이어서 실행
```

**예시:**
- "Phase 3에서 접근 방식 변경"
- "새 Phase 추가 필요"

### 4. 버그 수정

```
/debug [에러 메시지 또는 증상]
```

진단 → 수정 적용 → /review-comments(자동)
필요 시 사용자가 /build, /test 추가 실행.

**예시:**
- "BC1064 에러 발생"
- "유닛이 목표 지점에 도착해도 멈추지 않음"
- "Ghost 데이터가 클라이언트에 동기화되지 않음"

### 5. 코드 변경 전 영향 분석

```
/analyze [컴포넌트/시스템/기능 영역]
```

읽기 전용. 코드를 수정하지 않고 의존성 그래프와 영향 범위만 출력한다.

**예시:**
- "Health 컴포넌트에 Shield 필드 추가하면 어디가 영향받는지"
- "이동 시스템 전체 의존성 파악"
- "PredictedMovementSystem의 실행 순서 확인"

### 6. 구현 후 코드 리뷰

```
/review-code [선택: 파일/커밋 범위]
```

컨벤션, DOTS 규칙, 패턴 일관성 기준으로 변경 코드를 검토한다.

**예시:**
- 구현 완료 후 커밋 전 최종 점검
- 특정 파일만 집중 리뷰

### 7. 주석만 빠르게 정리

```
/review-comments [선택: 파일 경로]
```

변경된 파일의 주석이 코드와 일치하는지 점검하고 즉시 수정한다.
Tier 1 스킬이 자동 호출하므로, 단독 사용은 핫픽스 후 주석만 정리할 때.

### 8. 문서만 업데이트

```
/update-docs [선택: 범위]
```

코드 변경에 대응하는 Docs 문서를 갱신한다.
Tier 1 스킬이 자동 호출하므로, 단독 사용은 수동으로 코드를 수정한 후 문서만 맞출 때.

### 9. 빌드 검증

```
/build [선택: 타겟]
```

Unity Editor가 닫혀 있어야 실행 가능. 기본 타겟은 Win64.

### 10. 테스트 실행

```
/test [선택: 필터 또는 playmode]
```

Unity Editor가 닫혀 있어야 실행 가능. 기본은 전체 EditMode 테스트.

**예시:**
- `/test` - 전체 EditMode
- `/test playmode` - PlayMode 테스트
- `/test FlowFieldCoreTests` - 특정 테스트만

### 11. 커밋

```
/commit
```

변경사항을 분석하여 한국어 커밋 메시지를 작성하고, 사용자 확인 후 커밋한다.

---

## 일반적인 워크플로우 체이닝

### 소규모 작업

```
/implement → (/review-comments + /update-docs 자동) → /build → /test → /commit
```

### 대규모 작업

```
/plan-create → (/review-plan 자동)
/plan-execute → (/review-comments + /update-docs 자동) → /review-code → /build → /test → /commit
```

### 분석 후 구현

```
/analyze → /plan-create 또는 /implement
```

### 디버그 후 검증

```
/debug → (/review-comments 자동) → /build → /test → /commit
```
