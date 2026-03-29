---
name: analyze
description: ECS 시스템 간 의존성과 컴포넌트 변경 영향도를 분석합니다. 컴포넌트/시스템 수정 전 영향 범위를 파악하거나 아키텍처를 이해할 때 사용합니다.
allowed-tools: Read, Grep, Glob, Bash, Agent, AskUserQuestion
---

## 역할

지정된 컴포넌트, 시스템, 또는 기능 영역의 의존성과 영향 범위를 분석하여 보고한다.
코드 수정은 하지 않는다. 분석 결과만 출력한다.

$ARGUMENTS가 있으면 해당 내용을 분석 대상으로 반영한다. 없으면 `AskUserQuestion` 도구로 분석 유형을 선택지로 제시한다 (컴포넌트 / 시스템 / 기능 영역 / 변경 시나리오).

## 분석 절차

### 1단계: 분석 대상 확정

사용자가 지정한 대상을 아래 유형 중 하나로 분류한다:

| 유형 | 예시 | 분석 범위 |
|---|---|---|
| **컴포넌트** | `Health`, `MovementGoal` | 이 컴포넌트를 Read/Write하는 모든 시스템 |
| **시스템** | `PredictedMovementSystem` | 이 시스템이 접근하는 컴포넌트 + 전후 시스템 |
| **기능 영역** | "이동", "전투", "건설" | 해당 영역의 시스템 그래프 전체 |
| **변경 시나리오** | "Health에 Shield 필드 추가" | 영향받는 시스템/Authoring/Ghost/테스트 |

### 2단계: 정적 의존성 수집

병렬로 다음을 수집한다:

**A. 컴포넌트 접근 맵** (대상이 컴포넌트인 경우)
- Grep으로 대상 컴포넌트명을 검색하여 모든 참조 파일 수집
- 각 참조를 접근 유형으로 분류:
  - `RefRW<T>` / `RefRO<T>` → SystemAPI 직접 접근
  - `ComponentLookup<T>` → Random Access
  - `EnabledRefRW<T>` / `EnabledRefRO<T>` → Enableable 컴포넌트
  - `IJobEntity` 파라미터 → Job 접근
  - `EntityQuery` / `WithAll<T>` → 필터링
  - `AddComponent<T>` / `RemoveComponent<T>` → 구조 변경
  - `Authoring` / `Baker` → Baking 시점

**B. 시스템 실행 순서** (대상이 시스템인 경우)
- `[UpdateInGroup]`, `[UpdateAfter]`, `[UpdateBefore]` 어트리뷰트 수집
- `Docs/Systems/시스템 그룹 및 의존성.md`와 대조
- 같은 SystemGroup 내 다른 시스템이 동일 컴포넌트에 접근하는지 확인

**C. Ghost 동기화 경로** (대상이 Ghost 컴포넌트인 경우)
- `[GhostField]` 어트리뷰트 확인
- Server Write → Client Read 경로 추적
- Prediction 여부 (`[GhostField(Quantization=...)]` 등) 확인

**D. RPC/이벤트 연결**
- 대상과 관련된 RPC 수집 (`*Rpc.cs`)
- DamageEvent 등 버퍼 이벤트 연결 추적

### 3단계: 의존성 그래프 구성

수집된 데이터를 기반으로 의존성 그래프를 구성한다:

```
[시스템A] --Write--> [컴포넌트X] --Read--> [시스템B]
                                  --Read--> [시스템C]
[시스템D] --Structural Change--> [컴포넌트X]
```

### 4단계: 영향도 평가

**변경 시나리오가 주어진 경우**, 아래 항목을 평가한다:

| 영향 항목 | 확인 내용 |
|---|---|
| **컴파일 영향** | 필드/메서드 시그니처 변경으로 빌드 실패하는 파일 |
| **런타임 영향** | 실행 순서, 데이터 흐름 변경으로 동작이 바뀌는 시스템 |
| **네트워크 영향** | Ghost 동기화, RPC 페이로드 변경 |
| **Authoring 영향** | Baker, Prefab, SubScene 재baking 필요 여부 |
| **테스트 영향** | 기존 테스트 수정/추가 필요 여부 |

### 5단계: 분석 결과 출력

```
## 의존성 분석 결과

### 분석 대상
- 대상: [컴포넌트/시스템/영역명]
- 유형: [컴포넌트 / 시스템 / 기능 영역 / 변경 시나리오]

### 접근 맵
| 시스템 | 접근 유형 | 어셈블리 | SystemGroup |
|--------|-----------|----------|-------------|
| ...    | RefRW     | Server   | Simulation  |
| ...    | RefRO     | Client   | Presentation|

### 의존성 그래프
(텍스트 다이어그램)

### Ghost 동기화 경로 (해당 시)
| 필드 | 방향 | Quantization | Prediction |
|------|------|-------------|------------|
| ...  | S→C  | ...         | ...        |

### 영향도 평가 (변경 시나리오가 주어진 경우)
| 영향 항목 | 파일 | 설명 |
|-----------|------|------|
| 컴파일    | ...  | ...  |
| 런타임    | ...  | ...  |
| 네트워크  | ...  | ...  |

### 주의 사항
- (변경 시 특별히 주의해야 할 점)

### 권장 작업 순서
1. (변경이 필요한 경우, 안전한 수정 순서 제안)
```

## 주의사항

- **코드 수정 금지**: 이 스킬은 분석만 수행한다. 수정이 필요하면 사용자에게 안내하거나 다른 스킬(`/plan-create`, `/diagnose`)을 권장한다.
- **사실 기반**: Grep/Read로 확인한 내용만 보고한다. 추측은 "미확인"으로 표시한다.
- **Docs 우선 참조**: 분석 시작 전 `Docs/Systems/` 관련 문서를 읽어 현재 아키텍처를 파악한 뒤 코드를 탐색한다.
- **어셈블리 구분 명시**: 모든 시스템/컴포넌트에 Client/Server/Shared/Authoring 어셈블리를 표기한다.
