# 스킬 계층 구조

```
[Tier 1: 오케스트레이션] 사용자 호출 → Tier 2 자동 호출
  implement ──auto──→ /sync-comments, /sync-docs
  plan-execute ──auto──→ /sync-comments, /sync-docs
  diagnose ──auto──→ /sync-comments
  plan-create ──auto──→ /review-plan ──auto──→ /plan-edit

[Tier 2: 후처리/검증]
  sync-comments      주석 정합성 점검 및 수정
  sync-docs          Docs 문서 동기화
  test               EditMode/PlayMode 테스트
  review-code        3-에이전트 병렬 코드 리뷰 (DOTS/컨벤션/성능, worktree 격리)

[독립 도구]
  analyze            의존성/영향도 분석 (읽기 전용)
  commit             커밋 메시지 작성 및 커밋
  create-skill       기존 패턴 기반 새 스킬 생성
  plan-edit          계획 부분 수정
  review-plan        계획 검토
  review-design      기획 방향성 검증 (단독 실행용, /review에도 포함)
  review-skill       스킬 토큰 효율성/중복/적합성 점검 (읽기 전용)
```

## 공유 참조 문서

| 문서 | 참조 스킬 |
|------|-----------|
| `Docs/Checklists/pattern-search-guide.md` | implement, plan-execute, diagnose, review-code |
| `Docs/Checklists/review-checklist-dots.md` | review-code, review-plan |
| `Docs/Checklists/review-checklist-convention.md` | review-code, review-plan |
| `Docs/Checklists/review-checklist-perf.md` | review-code |
