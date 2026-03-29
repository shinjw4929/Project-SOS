---
name: diagnose
description: Unity DOTS 특화 디버깅 스킬. 에러 메시지/증상을 입력하면 관련 시스템을 탐색하고 DOTS 고유 원인을 체계적으로 점검하여 수정안을 제시합니다.
allowed-tools: Read, Edit, Grep, Glob, Bash, Agent, Skill, AskUserQuestion
---

## 역할

Unity DOTS 프로젝트에서 발생하는 에러, 비정상 동작, 크래시를 체계적으로 진단하고 수정안을 제시한다.

$ARGUMENTS가 있으면 해당 내용을 에러 메시지/증상/재현 조건으로 반영한다.

## 디버깅 절차

### 1단계: 증상 수집

사용자로부터 다음 정보를 확보한다. $ARGUMENTS에서 추출할 수 없는 항목은 사용자에게 질문한다.
**발생 시점**이 불명확한 경우 `AskUserQuestion` 도구로 선택지를 제시한다 (편집 모드 / 플레이 모드 / 빌드 후).

| 항목 | 설명 |
|---|---|
| **에러 메시지** | 콘솔 에러, 빌드 에러, 스택 트레이스 |
| **증상** | 예상 동작 vs 실제 동작 |
| **재현 조건** | 언제, 어떤 상황에서 발생하는지 |
| **발생 시점** | 편집 모드, 플레이 모드, 빌드 후 |

### 2단계: 에러 유형 분류 및 1차 탐색

에러를 아래 카테고리로 분류하고, 해당 카테고리의 점검 항목을 우선 확인한다.

**A. Burst 컴파일 에러**
- `BC1064` (external function): struct 파라미터/반환이 있는 `[BurstCompile]` static 메서드 → `[BurstCompile]` 제거, `[MethodImpl(AggressiveInlining)]` 적용
- `BC1091` (readonly violation): `RefRO` 데이터에 쓰기 시도
- `BC1045` (managed type): `string`, `class`, `List<T>` 등 managed 타입 사용
- `bool` blittable 에러: Burst struct에서 `bool` 필드를 `ref`로 전달 → `[MarshalAs(UnmanagedType.U1)]` 추가

**B. Ghost/Netcode 동기화 에러**
- `GhostField` 불일치: Client/Server 간 Ghost 컴포넌트 필드 불일치
- `GhostType` 오류: Prefab에 GhostAuthoringComponent 누락 또는 잘못된 설정
- RPC 방향 오류: Client→Server RPC를 Server에서 전송하거나 그 반대
- Authority 위반: Client에서 Server Authority 데이터 직접 수정

**C. ECB/엔티티 생명주기 에러**
- `InvalidOperationException` (entity doesn't exist): 이미 파괴된 엔티티 접근 → `EntityManager.Exists()` 체크 누락
- `ComponentLookup` 실패: `TryGetComponent` 대신 직접 접근 → null 체크 누락
- ECB 순서 문제: 같은 프레임에서 생성+수정 시 ECB Playback 순서 확인
- Baker 제약: 자식 GameObject 엔티티에 `AddComponent` 시도 → 런타임 시스템으로 이관

**D. SystemGroup/실행 순서 에러**
- 데이터 레이스: 같은 컴포넌트를 여러 시스템이 동시 접근 → `UpdateAfter`/`UpdateBefore` 누락
- Job 스케줄링 충돌: `CompleteDependency` 없이 같은 데이터 접근
- 싱글톤 미초기화: `SystemAPI.GetSingleton` 호출 시 싱글톤이 아직 생성되지 않음 → `TryGetSingleton` 사용

**E. Physics/Collider 에러**
- Collider 크기 불일치: ObstacleRadius와 Capsule 반지름 차이
- 물리 충돌을 Collider로 처리 (금지) → GridCell.IsPathBlocked 기반으로 전환

**F. 런타임 비정상 동작 (에러 메시지 없음)**
- 유닛이 움직이지 않음 → MovementGoal, FlowField, PredictedMovement 순서 추적
- 공격이 안 됨 → AggroTarget, CombatDamageSystem, DamageEvent 버퍼 확인
- 건설이 안 됨 → BuildArrivalSystem, MovementArrivalSystem 도착 판정 확인
- Ghost 데이터가 동기화되지 않음 → `[GhostField]` 어트리뷰트, GhostMode, Prefab 확인

### 3단계: 원인 추적

1. **스택 트레이스 분석**: 에러에 파일명/라인이 있으면 해당 코드를 Read
2. **관련 시스템 탐색**: 에러가 발생한 컴포넌트/시스템을 Grep으로 추적하여 의존 관계 파악
3. **SystemGroup 순서 확인**: `Docs/Systems/시스템 그룹 및 의존성.md`와 실제 어트리뷰트(`[UpdateInGroup]`, `[UpdateAfter]`)를 대조
4. **데이터 흐름 추적**: 문제 컴포넌트의 Write 지점 → Read 지점을 순서대로 추적

2단계에서 분류한 카테고리별 점검 항목을 체크리스트로 순차 확인하며, 원인을 좁혀간다.

### 4단계: 진단 결과 출력

```
## 디버깅 결과

### 증상 요약
- (에러 메시지 / 비정상 동작 요약)

### 원인 분석
| # | 원인 | 근거 (파일:라인) | 카테고리 |
|---|------|------------------|----------|
| 1 | ...  | ...              | Burst/ECB/Ghost/... |

### 수정안
| # | 파일:라인 | 현재 코드 | 수정 코드 | 설명 |
|---|-----------|-----------|-----------|------|
| 1 | ...       | ...       | ...       | ...  |

### 추가 확인 필요
- (수정 후에도 검증이 필요한 사항)
```

### 5단계: 수정 적용

사용자에게 수정 적용 여부를 확인한 뒤:
- **단순 라인 수정**: Edit 도구로 직접 수정한다.
- **새 시스템/컴포넌트 추가 등 큰 변경**: `Docs/Checklists/pattern-search-guide.md`를 따라 기존 패턴을 탐색한 뒤 수정한다. 또는 `/implement`로 패턴 기반 구현을 권장한다.
- 수정 적용 후 `/sync-comments`를 자동 호출하여 주석 정합성을 점검한다.
- `/build`로 컴파일 검증을 권장한다.
- 수정이 시스템 동작에 영향을 주는 경우 `/test`도 권장한다.

## 주의사항

- **사실 기반 진단**: 코드를 직접 읽고 확인한 내용만 원인으로 제시한다. 추측은 "가능성"으로 명시하고, 확인 방법을 함께 안내한다.
- **최소 변경**: 에러 수정에 필요한 최소한의 변경만 제안한다. 주변 코드 리팩토링은 하지 않는다.
- **CLAUDE.md 준수**: 수정 시 Development Guidelines(Burst 제약, GameSettings 패턴 등)를 따른다.
- **근본 원인 우선**: 증상만 가리는 workaround보다 근본 원인 수정을 우선한다. workaround가 필요한 경우 이유를 명시한다.
