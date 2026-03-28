# Phase 3: Garbage Collection - settings 정리

## 목표
- settings.local.json에서 일회성 권한을 제거하고 범용 패턴만 유지
- 권한 목록을 의미 단위로 정리하여 가독성 향상

## 선행 조건
- 없음 (Phase 1, 2와 병렬 가능)

## 작업 목록

### Task 1: 권한 분류
- [ ] 현재 84줄의 권한을 다음 범주로 분류:

**유지할 범용 패턴:**
- `Bash(git:*)`, `Bash(ls:*)`, `Bash(dir:*)`, `Bash(wc:*)`
- `Bash(mkdir:*)`, `Bash(mv:*)`, `Bash(for file:*)`, `Bash(while read:*)`
- `Bash(python:*)`, `Bash(python3:*)`, `Bash(py:*)`
- `Bash(dotnet build:*)`
- `Bash(docker ps:*)`, `Bash(docker exec:*)`
- `Bash(xargs:*)`, `Bash(iconv:*)`
- `WebSearch`
- `WebFetch(domain:docs.unity3d.com)`, `WebFetch(domain:discussions.unity.com)`
- `Skill(*)` (개별 스킬 대신 와일드카드)
- `Read` 프로젝트 경로

**제거할 일회성 권한 (~30개):**
- `Bash(del "C:\\\\...\\\\FlyingTag.cs")`
- `Bash(del "C:\\\\...\\\\EnemyDeathCountSystem.cs")`
- `Bash(rmdir ...)` 5개
- `Bash(mv "Docs/계획/..." ...)` 4개
- `Bash(mkdir -p "C:/Users/.../Assets/Shaders")` 등 특정 경로
- `Bash(python -c "import struct; hex_str=...")` (1회성 분석 명령)
- Unity 테스트 실행 명령 (범용화 필요)
- `Bash(2)`, `Bash(done)`, `Bash(do echo:*)` 등 무의미한 항목

**범용화할 항목:**
- Unity 테스트 경로 2개 → Unity 에디터 경로 와일드카드로 통합
- `Bash(findstr:*)`, `Bash(grep:*)`, `Bash(find:*)` → `Bash(findstr:*)` 수준으로 유지 (Grep 도구 권장이지만 fallback 용도)
- `Read(//c/Users/sjw49/Unity Projects/Project-SOS/**)` → 유지 (프로젝트 전체 읽기)

**제거할 비프로젝트 권한:**
- `Read(//c/Users/sjw49/Desktop/이력서/...)` 2개 → 프로젝트와 무관

### Task 2: settings.local.json 재작성
- [ ] 분류 결과를 반영하여 settings.local.json 재작성
- [ ] 카테고리별 주석은 넣지 않음 (JSON이므로)
- [ ] 알파벳/카테고리순 정렬

### Task 3: 글로벌 settings.json 확인
- [ ] `~/.claude/settings.json`의 `Bash(git:*)` 등이 프로젝트 settings와 중복되는지 확인
- [ ] 글로벌에 있는 권한은 로컬에서 제거

## 병렬 작업 구성

단일 파일 수정이므로 순차 실행.

## 테스트 요구사항

### 수동 검증
- 정리 후 일반적인 워크플로우에서 권한 에러가 발생하지 않는지 확인:
  - `git status`, `git diff`, `git add`, `git commit`
  - `ls`, `mkdir`, `mv`
  - `/commit`, `/review-code` 등 스킬 호출
  - Unity 관련 명령 (빌드, 테스트)

## 검증 방법
- settings.local.json 줄 수 <= 30
- 일회성 경로(`FlyingTag.cs`, `EnemyDeathCountSystem.cs`, `rmdir "Docs/계획"`) 포함 여부 Grep 확인
- 기존 워크플로우 시뮬레이션

## 완료 기준
- [ ] settings.local.json에서 일회성 권한 제거됨
- [ ] 범용 패턴만 남아있음 (~20줄)
- [ ] 비프로젝트 경로 권한 제거됨
- [ ] 글로벌/로컬 중복 제거됨
