# 하네스 엔지니어링 최적화 오케스트레이션 플랜

## 문제 정의

현재 Claude Code 하네스(.claude/, CLAUDE.md, 스킬)가 하네스 엔지니어링 핵심 원칙을 위반하고 있다:

1. **SSOT 위반**: 동일 규칙(커밋 형식, DOTS 규칙, GameSettings 패턴 등)이 CLAUDE.md와 스킬 3~4곳에 중복. 규칙 수정 시 모든 곳을 동시에 갱신해야 함.
2. **Progressive Disclosure 위반**: review-code 스킬(138줄)이 상세 체크리스트 테이블을 인라인으로 포함. 호출 시 전체가 컨텍스트에 주입되어 비효율적.
3. **권한 누적**: settings.local.json에 일회성 명령 권한이 84줄 누적. 범용 패턴과 구분 없이 혼재.
4. **Back-Pressure 부재**: 빌드/테스트를 강제하는 스킬이나 hooks가 없음. 에이전트의 자기 검증 메커니즘 부재.
5. **Context Firewall 미적용**: review-code가 모든 diff + Docs + 주변 코드를 단일 컨텍스트에 로드.
6. **Hooks 0개**: 자동 피드백 루프 메커니즘 전무.

### 영향 범위
- `CLAUDE.md` (93줄)
- `.claude/skills/` 7개 스킬 (561줄)
- `.claude/settings.local.json` (84줄)
- `Docs/Checklists/` (신규 디렉토리)

## AS-IS (현재 상태)

### CLAUDE.md (93줄)
- Role & Communication Style (8줄)
- Guidelines for Solutions (4줄)
- Operational Mandate + Pre/Post Checklist (14줄)
- Project Reference (2줄)
- Development Guidelines: 기본 원칙, Burst 제약, DOTS Rules, Combat Rules, Collider Rules (30줄)
- 커밋 메시지 작성 가이드 (19줄)

### 스킬 현황
| 스킬 | 줄 수 | 주요 내용 |
|------|-------|----------|
| commit | 45 | 커밋 절차 + 메시지 형식 (CLAUDE.md와 중복) |
| plan-create | 89 | 계획 생성 절차 + GameSettings 언급 |
| plan-execute | 101 | Phase 실행 절차 + DOTS 규칙 참조 |
| review-code | 138 | 5개 검토 테이블 인라인 (DOTS/네트워크/컨벤션/설계/품질) |
| review-comments | 49 | 주석 검토 절차 |
| review-plan | 88 | DOTS 규칙 + 네트워크 검토 (review-code와 중복) |
| update-docs | 51 | 문서 업데이트 매핑 테이블 (Documentation-Checklist.md와 중복) |

### 중복 매핑
| 규칙 | 위치 수 | 정의 장소 |
|------|---------|----------|
| 커밋 메시지 형식 | 2 | CLAUDE.md + commit 스킬 |
| BurstCompile 규칙 | 3 | CLAUDE.md + review-code + review-plan |
| GameSettings 패턴 | 4 | CLAUDE.md + review-code + review-plan + plan-create |
| DamageEvent 버퍼 | 3 | CLAUDE.md + review-code + review-plan |
| "User not Player" | 2 | CLAUDE.md + review-code |
| bool MarshalAs | 2 | CLAUDE.md + review-code |
| ECB 사용 | 3 | CLAUDE.md + review-code + review-plan |
| 문서 업데이트 매핑 | 2 | Documentation-Checklist.md + update-docs 스킬 |

### settings.local.json (84줄)
- 범용 패턴: `git:*`, `ls:*`, `python:*` 등 ~15개
- 일회성 명령: `del`, `rmdir`, `mv`, 특정 경로 `mkdir` 등 ~30개
- WebFetch 도메인: 4개
- Skill 허용: 4개
- Read 허용: 3개 (이력서 경로 등 비프로젝트)

### Hooks
없음.

### 누락 스킬
- `/build` (Unity CLI 빌드)
- `/test` (EditMode/PlayMode 테스트)

## TO-BE (목표 상태)

### CLAUDE.md
- **규칙의 유일한 정의 장소** (SSOT). Development Guidelines 유지.
- 커밋 메시지 형식 제거 (commit 스킬에 위임).
- Post-Implementation Checklist에서 WorkLog 기록 항목 제거 (plan-execute는 execution-log 사용, 단발 작업은 선택적).

### 스킬
- **절차(How)만 기술**. 인라인 규칙 테이블 제거.
- "CLAUDE.md Development Guidelines 기준으로 검증" + "Docs/Checklists/ 참조" 패턴.
- review-code: 체크리스트 외부화 + 서브에이전트 파일별 검토.
- update-docs: Documentation-Checklist.md 참조로 단순화.
- 신규: `/build`, `/test` 스킬.

### Docs/Checklists/ (신규)
- `review-code-checklist.md`: review-code 검토 항목 테이블 5개 통합.
- 기존 `Documentation-Checklist.md`는 유지 (update-docs가 참조).

### settings.local.json
- 일회성 권한 제거, 범용 패턴만 유지 (~20줄).

### Hooks
- 커밋 전 Unity 빌드 검증 등 피드백 루프 구성 (Unity CLI 빌드 속도 고려하여 선택적 적용).

## AS-IS vs TO-BE 비교표

| 항목 | AS-IS | TO-BE |
|------|-------|-------|
| 규칙 정의 | CLAUDE.md + 스킬 3~4곳에 중복 | CLAUDE.md 단일 정의 (SSOT) |
| review-code 체크리스트 | 138줄 인라인 | ~40줄 절차 + 외부 체크리스트 Read |
| review-plan 검토 기준 | 인라인 DOTS/네트워크 테이블 | CLAUDE.md + 체크리스트 참조 |
| update-docs 매핑 | 인라인 테이블 (Documentation-Checklist과 중복) | Documentation-Checklist.md 참조 |
| commit 메시지 형식 | CLAUDE.md + 스킬 양쪽 | commit 스킬에만 |
| settings.local.json | 84줄 (일회성 포함) | ~20줄 (범용 패턴만) |
| 빌드/테스트 스킬 | 없음 | /build, /test |
| Hooks | 0개 | 커밋 전 검증 등 |
| 서브에이전트 전략 | 단일 컨텍스트 | 파일별 서브에이전트 위임 |
| 전체 스킬 줄 수 | 561줄 | ~350줄 (외부 참조 포함) |

## Phase 체크리스트

### Phase 1: SSOT 확립 - 중복 제거
- [x] CLAUDE.md에서 커밋 메시지 형식 섹션 제거
- [x] commit 스킬이 커밋 형식의 유일한 정의 장소가 됨을 확인
- [x] review-code에서 인라인 규칙 테이블을 CLAUDE.md 참조로 교체
- [x] review-plan에서 인라인 DOTS/네트워크 테이블을 참조로 교체
- [x] plan-create/plan-execute에서 인라인 규칙 언급을 참조로 교체
- [x] update-docs에서 매핑 테이블을 Documentation-Checklist.md 참조로 교체
> 상세: [phase-1-ssot.md](./phase-1-ssot.md)

### Phase 2: Progressive Disclosure - 체크리스트 외부화
- [x] Docs/Checklists/ 디렉토리 생성
- [x] review-code-checklist.md 작성 (5개 테이블 통합)
- [x] review-code SKILL.md를 절차 중심으로 경량화 (73줄, 출력 템플릿 포함)
- [x] review-plan SKILL.md를 절차 중심으로 경량화 (66줄)
- [x] Documentation-Checklist.md 구조 정비 (Checklists/ 디렉토리 추가)
> 상세: [phase-2-progressive-disclosure.md](./phase-2-progressive-disclosure.md)

### Phase 3: Garbage Collection - settings 정리
- [x] settings.local.json에서 일회성 권한 제거
- [x] 범용 패턴으로 통합 정리
- [x] 비프로젝트 경로(이력서 등) 권한 제거
> 상세: [phase-3-garbage-collection.md](./phase-3-garbage-collection.md)

### Phase 4: Back-Pressure - 신규 스킬
- [x] /build 스킬 생성 (Unity CLI 빌드 + 오류 파싱)
- [x] /test 스킬 생성 (EditMode/PlayMode 테스트 실행)
> 상세: [phase-4-back-pressure.md](./phase-4-back-pressure.md)

### Phase 5: Context Firewall + Hooks
- [x] review-code 서브에이전트 전략 적용
- [x] hooks 설정 (Stop 알림 hook)
> 상세: [phase-5-firewall-hooks.md](./phase-5-firewall-hooks.md)

## Phase 간 의존성

| Phase | 의존성 | 병렬 가능 |
|-------|--------|----------|
| 1 (SSOT) | 없음 | - |
| 2 (Progressive Disclosure) | Phase 1 | X |
| 3 (Garbage Collection) | 없음 | O (Phase 1, 2와 병렬) |
| 4 (Back-Pressure) | 없음 | O (Phase 1, 2와 병렬) |
| 5 (Firewall + Hooks) | Phase 2, 3, 4 | X |

## 변경 파일 요약

| Phase | 파일 | 변경 |
|-------|------|------|
| 1 | `CLAUDE.md` | 커밋 메시지 섹션 제거 |
| 1 | `.claude/skills/review-code/SKILL.md` | 인라인 테이블 → 참조 |
| 1 | `.claude/skills/review-plan/SKILL.md` | 인라인 테이블 → 참조 |
| 1 | `.claude/skills/plan-create/SKILL.md` | 인라인 규칙 → 참조 |
| 1 | `.claude/skills/plan-execute/SKILL.md` | 인라인 규칙 → 참조 |
| 1 | `.claude/skills/update-docs/SKILL.md` | 매핑 테이블 → 참조 |
| 2 | `Docs/Checklists/review-code-checklist.md` | 신규 생성 |
| 2 | `.claude/skills/review-code/SKILL.md` | 절차 중심 경량화 |
| 2 | `.claude/skills/review-plan/SKILL.md` | 절차 중심 경량화 |
| 3 | `.claude/settings.local.json` | 일회성 제거, 범용 정리 |
| 4 | `.claude/skills/build/SKILL.md` | 신규 생성 |
| 4 | `.claude/skills/test/SKILL.md` | 신규 생성 |
| 5 | `.claude/skills/review-code/SKILL.md` | 서브에이전트 전략 추가 |
| 5 | `.claude/settings.local.json` | hooks 설정 추가 |

## 검증 방법

1. 각 Phase 완료 후 변경된 스킬을 `/skill-name` 으로 호출하여 정상 동작 확인
2. CLAUDE.md 내용이 스킬에서 참조할 수 있는 구조인지 확인
3. review-code-checklist.md가 Read로 정상 로드되는지 확인
4. settings.local.json 정리 후 기존 워크플로우(git, build, test) 정상 작동 확인
5. /build, /test 스킬이 Unity CLI와 정상 연동되는지 확인

## 롤백 전략

- 모든 변경은 `.claude/` 및 `Docs/` 파일 수정이므로 git 단위 롤백 가능
- Phase별 커밋으로 개별 롤백 지원
- 스킬 내용 변경은 즉시 반영되므로 문제 발생 시 원본 복원으로 즉각 복구
