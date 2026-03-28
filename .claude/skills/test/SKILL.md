---
name: test
description: Unity EditMode/PlayMode 테스트를 실행하고 결과를 보고합니다.
allowed-tools: Bash, Read, Grep, Glob
---

## Unity 테스트 실행

$ARGUMENTS 파싱:
- 인자 없음: 전체 EditMode 테스트
- `playmode`: PlayMode 테스트
- 테스트 필터 (예: `Tests.EditMode.Utilities.FlowFieldCoreTests`): 해당 필터만 실행

### 1단계: 사전 확인

1. Unity Editor 프로세스 확인:
   ```
   tasklist /FI "IMAGENAME eq Unity.exe" 2>/dev/null | grep -q Unity
   ```
   - 실행 중이면 "Unity Editor가 열려 있어 배치 모드 테스트를 실행할 수 없습니다. Editor를 닫고 다시 시도해 주세요." 출력 후 중단.

2. Unity Editor 경로 탐색:
   ```
   ls "/c/Program Files/Unity/Hub/Editor/"
   ```

3. TestResults 디렉토리 확보:
   ```
   mkdir -p TestResults
   ```

### 2단계: 테스트 실행

```
"<Unity경로>/Unity.exe" -runTests -batchmode -projectPath "." \
  -testPlatform <EditMode|PlayMode> \
  [-testFilter "<필터>"] \
  -testResults "TestResults/results.xml" \
  -logFile "TestResults/test.log"
```

- timeout: 600000ms (10분)
- `run_in_background`로 실행

### 3단계: 결과 파싱

`TestResults/results.xml`을 Read하여 파싱:
- 전체 테스트 수, 통과, 실패, 스킵 카운트 추출
- 실패한 테스트의 이름과 오류 메시지 추출

XML이 없거나 파싱 실패 시 `TestResults/test.log`에서 오류를 직접 탐색.

### 4단계: 결과 출력

- **전체 통과**: "테스트 통과 (N개)" 한 줄 출력
- **실패 있음**:
  ```
  테스트 결과: N개 통과 / M개 실패 / K개 스킵

  실패한 테스트:
  | # | 테스트명 | 오류 메시지 |
  |---|---------|-----------|
  | 1 | ...     | ...       |
  ```

### 주의사항

- Unity Editor가 열려 있으면 실행 불가. 반드시 1단계에서 확인.
- PlayMode 테스트는 EditMode보다 시간이 오래 걸릴 수 있다.
