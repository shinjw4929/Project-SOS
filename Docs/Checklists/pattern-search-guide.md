# 패턴 탐색 가이드

> 코드 작성 전 기존 패턴을 탐색하고 추출하는 공유 절차.
> `/implement`, `/plan-execute`, `/debug` 스킬이 코드 작성 시 참조한다.

## 1. 유형별 탐색 전략

구현 대상의 유형을 분류하고, 해당 유형에 맞는 레퍼런스 코드를 탐색한다.

| 유형 | 탐색 대상 | 탐색 방법 |
|---|---|---|
| **ISystem** | 같은 SystemGroup 내 기존 시스템 | `[UpdateInGroup(typeof(TargetGroup))]` Grep → 가장 유사한 시스템 Read |
| **IJobEntity** | 동일 컴포넌트를 다루는 기존 Job | 대상 컴포넌트명 Grep → Job 구현 Read |
| **IComponentData** | 같은 도메인의 기존 컴포넌트 | 같은 폴더 내 컴포넌트 Glob → 구조 확인 |
| **Baker/Authoring** | 유사한 기존 Authoring 클래스 | `Assets/Scripts/Authoring/` Glob → 패턴 Read |
| **유틸리티** | `Assets/Scripts/Shared/Utilities/` 내 기존 유틸리티 | 해당 폴더 Glob → 시그니처 패턴 Read |
| **테스트** | `Assets/Tests/` 내 같은 영역의 기존 테스트 | 해당 폴더 Glob → 테스트 구조 Read |
| **기존 파일 수정** | 수정 대상 파일 + 관련 시스템 | 대상 파일 Read + 대상이 사용하는 컴포넌트/시스템 Grep |

## 2. 탐색 규칙

- 레퍼런스는 **최소 1개, 최대 3개**를 선정한다. 가장 유사한 것을 우선한다.
- 기존 파일을 수정하는 경우, **해당 파일 자체가 1순위 레퍼런스**다.
- 유사한 패턴이 없으면, 없다고 판단하고 CLAUDE.md 규칙과 일반 DOTS 관례에 따른다.

## 3. 패턴 추출 항목

레퍼런스에서 다음 4가지를 추출한다:

### A. 구조 패턴
- 클래스/struct 선언 방식 (`partial struct` + `[BurstCompile]` + `[UpdateInGroup]` 등)
- 필드 선언 순서 (Lookup → 일반 필드 → 설정값)
- 메서드 분할 방식 (`OnCreate` → `OnUpdate` → private 헬퍼)

### B. 데이터 접근 패턴
- `SystemAPI.GetSingleton` vs `TryGetSingleton` 사용 위치
- `ComponentLookup` 초기화 및 갱신 패턴 (`state.GetComponentLookup` + `Update(ref state)`)
- ECB 획득 패턴 (`SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()`)
- `RefRO`/`RefRW` 선택 기준

### C. 네이밍 패턴
- 시스템명: `[동작][대상]System` (예: `HandleMoveRequestSystem`)
- Job명: `[동작][대상]Job` (예: `ApplyDamageJob`)
- 컴포넌트명: 명사 또는 명사구 (예: `MovementGoal`, `AggroTarget`)
- 유틸리티: `[도메인]Utility` (예: `WanderUtility`)

### D. GameSettings 패턴
- 밸런스/규칙 상수는 `GameSettings` 싱글톤에서 읽는다.
- `SystemAPI.TryGetSingleton<GameSettings>(out var gs) ? gs.Field : DEFAULT` 패턴.
- Job 내부에서는 `OnUpdate`에서 읽어 struct 필드로 전달.

## 4. 적용 원칙

- **레퍼런스 구조 준수**: 레퍼런스와 동일한 어트리뷰트 배치, 필드 순서, 메서드 구조를 따른다.
- **기존 패턴 우선**: 프로젝트의 기존 패턴이 일반적인 모범 사례와 다르더라도, 일관성을 위해 기존 패턴을 따른다. 단, 패턴이 명백히 버그를 유발하는 경우는 사용자에게 알린다.
- **파일 배치**: 새 파일 생성 시, 같은 유형의 기존 파일이 있는 디렉토리에 배치한다.
