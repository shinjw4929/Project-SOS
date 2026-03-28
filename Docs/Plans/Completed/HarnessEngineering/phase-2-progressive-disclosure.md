# Phase 2: Progressive Disclosure - 체크리스트 외부화

## 목표
- review-code의 상세 검토 테이블을 외부 파일로 분리
- 스킬 본문은 **절차 흐름만** 유지하여 컨텍스트 효율 극대화
- 필요한 시점에 Read로 체크리스트를 가져오는 Progressive Disclosure 패턴 적용

## 선행 조건
- Phase 1 완료 (스킬에서 인라인 규칙이 참조로 교체된 상태)

## 작업 목록

### Task 1: Docs/Checklists/ 디렉토리 및 체크리스트 생성
- [ ] `Docs/Checklists/` 디렉토리 생성
- [ ] `Docs/Checklists/review-code-checklist.md` 작성:
  - review-code 스킬의 3단계에서 추출한 5개 테이블 (A~E) 통합
  - A. DOTS 규칙 (10개 항목)
  - B. 네트워크 아키텍처 (4개 항목)
  - C. 프로젝트 컨벤션 (8개 항목)
  - D. 시스템 설계 (4개 항목)
  - E. 코드 품질 (3개 항목)
  - 각 항목: 검토 항목 | 위반 조건 형식 유지
- [ ] `Docs/Documentation-Checklist.md`의 Docs 폴더 구조 섹션에 `Checklists/` 디렉토리 추가

### Task 2: review-code 스킬 경량화 + 체크리스트 참조 추가
- [ ] Phase 1에서 CLAUDE.md만 참조하도록 교체된 3단계에 체크리스트 참조를 추가:
  ```
  CLAUDE.md의 Development Guidelines + Docs/Checklists/review-code-checklist.md를
  Read하여 검토 기준으로 사용한다.
  ```
- [ ] SKILL.md를 ~40줄로 최종 축소:
  ```
  1단계: git diff/status로 변경사항 수집 (현재와 동일)
  2단계: Docs/Documentation-Checklist.md 참조하여 관련 문서 읽기
  3단계: Docs/Checklists/review-code-checklist.md를 Read하여 검토 실행
         + CLAUDE.md Development Guidelines 기준 적용
  4단계: 결과 출력 (심각도 기준 + 형식은 현재와 동일)
  5단계: 후속 행동
  ```
- [ ] 심각도 기준(치명/경고/제안)은 스킬 본문에 유지 (절차 흐름에 해당)
- [ ] "변경된 코드만 리뷰" 등 주의사항은 스킬 본문에 유지

### Task 3: review-plan 스킬 경량화 + 체크리스트 참조 추가
- [ ] Phase 1에서 CLAUDE.md만 참조하도록 교체된 검토 기준에 체크리스트 참조를 추가:
  ```
  코드 리뷰 체크리스트(Docs/Checklists/review-code-checklist.md)도 함께 참조한다.
  ```
- [ ] SKILL.md를 ~50줄로 최종 축소:
  ```
  1단계: 프로젝트 컨벤션 확인 → Docs/ 참조
  2단계: Docs/Checklists/review-code-checklist.md + CLAUDE.md 기준으로 검증
         (아키텍처, 중복/충돌, DOTS, 네트워크, 누락 관점)
  3단계: 결과 출력 (현재 형식 유지)
  4단계: 판단별 후속 행동 (현재와 동일)
  ```
- [ ] 개별 검토 항목 나열 제거, "체크리스트 참조" 패턴으로 통일

## 병렬 작업 구성

| Agent | 작업 내용 | 의존성 |
|-------|----------|--------|
| Agent A | Task 1 (체크리스트 파일 생성) | 없음 |
| Agent B | Task 2 + 3 (스킬 경량화) | Task 1 완료 후 |

Task 1이 완료되어야 Task 2, 3에서 참조 경로가 유효하므로 순차 실행.

## 테스트 요구사항

### 수동 검증
- `/review-code` 호출 시 체크리스트 파일을 Read로 가져오는지 확인
- 체크리스트 내용이 빠짐없이 외부 파일에 포함되었는지 원본 대조
- `/review-plan` 호출 시 체크리스트 참조가 정상 작동하는지 확인

## 검증 방법
- review-code SKILL.md 줄 수 <= 50
- review-plan SKILL.md 줄 수 <= 60
- `Docs/Checklists/review-code-checklist.md`에 29개 검토 항목 모두 포함 확인
- 스킬 호출 시 체크리스트 Read가 정상 실행되는지 확인

## 완료 기준
- [ ] `Docs/Checklists/review-code-checklist.md` 생성됨 (29개 항목)
- [ ] review-code SKILL.md <= 50줄
- [ ] review-plan SKILL.md <= 60줄
- [ ] 체크리스트 내용 누락 없음 (원본 대조 완료)
- [ ] 스킬 내 인라인 검토 테이블 완전 제거됨
