---
name: update-docs
description: 코드 변경사항을 분석하여 관련 Docs 문서를 업데이트합니다.
allowed-tools: Read, Edit, Grep, Glob, Bash
---

## 문서 업데이트 실행

$ARGUMENTS가 있으면 해당 내용을 업데이트 범위/대상으로 반영한다.

### 1단계: 변경사항 파악

다음을 병렬로 실행하여 변경 내용을 파악한다:
- `git diff HEAD --name-only` (staged + unstaged 변경 파일 목록)
- `git diff HEAD` (staged + unstaged 전체 변경 내용)
- `git status` (untracked 새 파일 확인, `-uall` 플래그 사용 금지)

### 2단계: 업데이트 대상 문서 결정

`Docs/Documentation-Checklist.md`의 **변경 유형별 업데이트 대상** 테이블을 따른다:

| 변경 유형 | 업데이트 대상 문서 |
|----------|-------------------|
| 새 시스템 추가 | `Docs/Systems/시스템 그룹 및 의존성.md`, `Docs/Systems/코드베이스 구조.md` |
| 새 컴포넌트 추가 | `Docs/Systems/코드베이스 구조.md`, 관련 기능 문서 |
| 선택 로직 변경 | `Docs/Systems/엔티티 선택 시스템.md` |
| 이동 로직 변경 | `Docs/Systems/엔티티 이동 시스템(navmesh).md` |
| 전투 로직 변경 | `Docs/Systems/엔티티 전투.md` |
| 건설 로직 변경 | `Docs/Systems/건설 시스템.md` |
| 자원/채집 변경 | `Docs/Systems/자원 채집 시스템.md`, `Docs/Systems/유저 자원, 인구수.md` |
| UI 상태 변경 | `Docs/Systems/Project-SOS 상태 시스템 설계.md` |
| 로깅 변경 | `Docs/Systems/로깅 시스템.md` |
| 새 RPC 추가 | `Docs/Systems/코드베이스 구조.md` (RPCs 섹션), 관련 기능 문서 |

주요 패턴, 시스템 플로우, 네이밍 규칙 등이 변경된 경우 `Docs/Architecture.md`와 `CLAUDE.md`도 함께 업데이트한다.

### 3단계: 대상 문서 읽기

업데이트 대상 문서를 모두 읽어 현재 내용을 파악한다.

### 4단계: 문서 업데이트

**작성 원칙**:
1. **코드와 동기화**: 실제 코드와 일치하도록 작성
2. **간결함 유지**: 핵심 로직과 데이터 흐름 중심
3. **기존 형식 유지**: 각 문서의 기존 마크다운 형식과 스타일을 따름
4. **변경 범위 최소화**: 변경된 부분만 수정, 불필요한 재작성 금지

### 5단계: 사용자에게 변경 내용 요약

업데이트한 문서 목록과 각 문서의 변경 내용을 간략히 보고한다.
