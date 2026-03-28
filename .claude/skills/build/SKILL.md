---
name: build
description: Unity 프로젝트를 CLI 배치 모드로 빌드하고 오류를 파싱합니다.
allowed-tools: Bash, Read, Grep, Glob
---

## Unity 빌드 실행

$ARGUMENTS가 있으면 빌드 타겟(Win64, Android 등)으로 반영한다. 기본값: Win64.

### 1단계: 사전 확인

1. Unity Editor 프로세스가 실행 중인지 확인:
   ```
   tasklist /FI "IMAGENAME eq Unity.exe" 2>/dev/null | grep -q Unity
   ```
   - 실행 중이면 "Unity Editor가 열려 있어 배치 모드 빌드를 실행할 수 없습니다. Editor를 닫고 다시 시도해 주세요." 출력 후 중단.

2. Unity Editor 경로 탐색:
   ```
   ls "/c/Program Files/Unity/Hub/Editor/"
   ```
   - 가장 최신 버전의 `Editor/Unity.exe` 경로를 사용한다.

### 2단계: 빌드 실행

```
"<Unity경로>/Unity.exe" -batchmode -nographics -projectPath "." -buildTarget <타겟> -logFile "build.log" -quit
```

- timeout: 600000ms (10분)
- `run_in_background`로 실행하여 빌드 완료 대기

### 3단계: 결과 파싱

`build.log`에서 오류/경고를 파싱한다:

1. **오류 탐색**: `error CS`, `Error`, `Fatal` 패턴
2. **경고 탐색**: `warning CS` 패턴 (선택적, 사용자가 요청한 경우)

### 4단계: 결과 출력

- **성공**: "빌드 성공" 한 줄 출력
- **실패**: 오류 목록을 `파일:라인 | 오류코드 | 메시지` 형식으로 출력

### 주의사항

- Unity Editor가 열려 있으면 라이선스 잠금으로 실행 불가. 반드시 1단계에서 확인.
- 빌드 로그가 매우 클 수 있으므로 오류 부분만 추출하여 출력한다.
