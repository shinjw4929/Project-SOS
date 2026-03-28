# Phase 4: Back-Pressure - 신규 스킬

## 목표
- `/build`, `/test` 스킬을 생성하여 에이전트가 자신의 작업을 검증할 수 있는 Back-Pressure 메커니즘 확보
- 다른 스킬(plan-execute, review-code)에서 검증 단계로 호출 가능

## 선행 조건
- 없음 (Phase 1, 2와 병렬 가능)

## 작업 목록

### Task 1: Unity 에디터 경로 및 빌드 명령 확인
- [ ] 현재 설치된 Unity 에디터 경로 확인 (`/c/Program Files/Unity/Hub/Editor/`)
- [ ] Unity CLI 빌드 명령 형식 확인:
  ```
  Unity.exe -batchmode -nographics -projectPath "." -buildTarget Win64 -logFile "build.log" -quit
  ```
- [ ] Unity CLI 테스트 명령 형식 확인:
  ```
  Unity.exe -runTests -batchmode -projectPath "." -testPlatform EditMode -testResults "TestResults/results.xml" -logFile "TestResults/test.log"
  ```
- [ ] 빌드/테스트 소요 시간 측정 (hooks에서 매번 실행할지 판단 근거)
- [ ] **Unity Editor 프로세스 감지**: Editor가 열려 있으면 라이선스 잠금으로 batchmode 실행 불가. 스킬에서 `tasklist /FI "IMAGENAME eq Unity.exe"` 등으로 프로세스 존재 여부를 사전 확인하고, 열려 있으면 안내 메시지 출력 후 중단하는 로직 포함

### Task 2: /build 스킬 생성
- [ ] `.claude/skills/build/SKILL.md` 작성:
  ```
  name: build
  description: Unity 프로젝트를 CLI로 빌드하고 오류를 파싱합니다.
  allowed-tools: Bash, Read, Grep
  ```
- [ ] 스킬 절차:
  1. Unity 에디터 경로 탐색 (Hub/Editor/ 아래 최신 버전)
  2. batchmode 빌드 실행
  3. 빌드 로그에서 오류/경고 파싱
  4. 성공 시 "빌드 성공" 한 줄 출력 (성공=무음 원칙)
  5. 실패 시 오류 목록 + 관련 파일 위치 출력
- [ ] $ARGUMENTS로 빌드 타겟(Win64, Android 등) 지정 가능

### Task 3: /test 스킬 생성
- [ ] `.claude/skills/test/SKILL.md` 작성:
  ```
  name: test
  description: Unity EditMode/PlayMode 테스트를 실행하고 결과를 보고합니다.
  allowed-tools: Bash, Read, Grep
  ```
- [ ] 스킬 절차:
  1. Unity 에디터 경로 탐색
  2. $ARGUMENTS 파싱:
     - 필터 없음: 전체 EditMode 테스트
     - 특정 필터: `-testFilter "namespace.class"` 적용
     - `playmode`: PlayMode 테스트 실행
  3. 테스트 실행 (batchmode)
  4. XML 결과 파싱 (통과/실패/스킵 카운트)
  5. 성공 시 한 줄 요약
  6. 실패 시 실패한 테스트 목록 + 오류 메시지 출력

## 병렬 작업 구성

| Agent | 작업 내용 | 의존성 |
|-------|----------|--------|
| Agent A | Task 1 (환경 확인) | 없음 |
| Agent B | Task 2 (/build 스킬) | Task 1 완료 후 |
| Agent C | Task 3 (/test 스킬) | Task 1 완료 후 |

Task 2, 3은 서로 다른 파일이므로 병렬 가능.

## 테스트 요구사항

### 수동 검증
- `/build` 호출 시 Unity CLI 빌드가 실행되는지 확인
- `/test` 호출 시 EditMode 테스트가 실행되는지 확인
- 빌드 실패 시 오류 파싱이 정확한지 확인
- 테스트 결과 XML 파싱이 정확한지 확인

## 검증 방법
- build SKILL.md, test SKILL.md 파일 존재 확인
- 각 스킬의 Unity 에디터 경로 탐색 로직이 동작하는지 확인
- 실제 빌드/테스트 실행 후 출력 형식 확인

## 완료 기준
- [ ] `.claude/skills/build/SKILL.md` 생성됨
- [ ] `.claude/skills/test/SKILL.md` 생성됨
- [ ] /build 호출 시 빌드 실행 + 결과 파싱 정상
- [ ] /test 호출 시 테스트 실행 + 결과 파싱 정상
- [ ] 성공=간결 출력, 실패=상세 출력 원칙 적용됨
