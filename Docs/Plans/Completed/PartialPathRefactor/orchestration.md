# PartialPathRefactor 오케스트레이션 플랜

## 문제 정의

1. **EnemyBig 벽 앞 영구 정지**: `IsPathPartial=true` 상태에서 EnemyTargetJob이 `if (!IsPathPartial)` 가드로 dest 갱신을 억제. FlowFieldSystem이 도달 가능 여부를 재평가할 기회가 없음.
2. **IsPathPartial 영구 고착**: FlowFieldSystem Apply 성공 시 `IsDestAdjusted==1`일 때만 true 설정하고 false 클리어 없음. 한번 true가 되면 영구 유지.
3. **타겟 이동/접근 무반응**: 유닛이 벽 밖으로 나와도, 다른 유저 유닛이 접근해도 적이 반응하지 않음. `needNewTarget=false`로 타겟 탐색 자체가 실행되지 않음.

**영향 범위**: UnifiedTargetingSystem (EnemyTargetJob, TargetPropagationJob), FlowFieldSystem, EnemyState, GameSettings

## AS-IS (현재 상태)

### 관련 파일
| 파일 | 역할 |
|------|------|
| `Assets/Scripts/Server/Systems/Combat/UnifiedTargetingSystem.cs` | EnemyTargetJob, TargetPropagationJob |
| `Assets/Scripts/Server/Systems/Movement/FlowFieldSystem.cs` | BFS, Apply, wallEdge fallback |
| `Assets/Scripts/Shared/Components/State/EnemyState.cs` | EnemyContext enum + CurrentState |
| `Assets/Scripts/Shared/Components/Movement/MovementGoal.cs` | Destination, IsPathPartial, DestinationSetTime 등 |
| `Assets/Scripts/Shared/Utilities/MovementMath.cs` | ShouldRetryPartialPath (2초 주기) |
| `Assets/Scripts/Shared/Utilities/WanderUtility.cs` | CheckStuck, CalculateDormantWakeTime |

### 현재 동작 방식

**EnemyTargetJob 섹션 1 (L286-318) — 유효 타겟 + 범위 내 처리**:
1. `IsPathPartial=true` → dest 갱신 억제 (`if (!IsPathPartial)` 가드, L302)
2. `ShouldRetryPartialPath` (2초 주기) → dest=targetPos 재설정, `IsPathDirty=true` (L310-318)
3. FlowFieldSystem → BFS 실패 → wallEdge fallback → `IsPathPartial=true` 재설정
4. `needNewTarget=false` → L323 `return` → 타겟 탐색 미실행
5. 결과: 적이 벽 앞에서 영구 정지, 근처 유닛에 무반응

**FlowFieldSystem Apply (L652-665) — 성공 경로**:
- `IsDestAdjusted==1` → `IsPathPartial=true` 설정
- `IsDestAdjusted==0` → **아무것도 안 함** (기존 true 유지)
- 결과: IsPathPartial 한번 true → 영구 고착

**AggroLock 섹션 (L232-250)**:
- 동일한 `IsPathPartial` dest 억제 + `ShouldRetryPartialPath` 패턴

## TO-BE (목표 상태)

### 핵심 변경
**dest를 항상 targetPos로 갱신. FlowFieldSystem이 매 프레임 wallEdge 조정 (LRU 캐시 히트).**

1. **EnemyTargetJob**: `if (!IsPathPartial)` dest 억제 제거 → dest를 매 프레임 targetPos로 갱신
2. **EnemyTargetJob**: `ShouldRetryPartialPath` 호출 2곳 제거 (dest가 항상 최신이므로 불필요)
3. **FlowFieldSystem Apply**: `IsDestAdjusted==0`이면 `IsPathPartial=false` 클리어
4. **EnemyTargetJob**: `IsPathPartial && Chasing` 시 프레임 분산(4프레임 1회) 대체 타겟 탐색
5. **EnemyState**: `AbandonedTarget` + `AbandonedExpireTime` 필드 추가 (stuck fallback)
6. **GameSettings**: `TargetAbandonDuration` 필드 추가 (기본 30초)

### 목표 동작
```
[매 프레임]
EnemyTargetJob: dest = targetPos → IsPathDirty = true
    ↓
FlowFieldSystem:
    ├─ 타겟이 벽 밖 → BFS 성공, IsDestAdjusted=0 → IsPathPartial=false → 정상 추격
    └─ 타겟이 벽 안 → BFS 실패 → wallEdge fallback → IsPathPartial=true → 벽 경계로 이동
    ↓
[IsPathPartial=true && Chasing 시, 4프레임 1회]
대체 타겟 탐색 (현재 타겟 제외)
    ├─ 대체 발견 → 즉시 전환
    └─ 대체 없음 → 현재 타겟 유지
    ↓
[~6초 후 stuck 감지]
AbandonedTarget에 기록 (30초 임시 차단) → Wandering 전환
```

## AS-IS vs TO-BE 비교표

| 항목 | AS-IS | TO-BE |
|------|-------|-------|
| dest 갱신 | IsPathPartial이면 억제 | 매 프레임 targetPos로 갱신 |
| IsPathPartial 클리어 | 거의 불가 (영구 고착) | FlowFieldSystem Apply에서 자동 클리어 |
| 타겟 이동 감지 | ShouldRetryPartialPath (2초 주기) | 매 프레임 즉시 반영 |
| 대체 타겟 탐색 | 없음 (needNewTarget=false) | 프레임 분산 주기적 탐색 |
| 벽 앞 정지 → 탈출 | 없음 (영구 정지) | stuck 감지 → AbandonedTarget 차단 → Wandering |

## Phase 체크리스트

### Phase 1: IsPathPartial 재평가 기반 구축
- [x] FlowFieldSystem Apply: IsDestAdjusted==0이면 IsPathPartial=false 클리어
- [x] EnemyTargetJob: IsPathPartial dest 억제 제거 (if (!IsPathPartial) 가드 삭제)
- [x] EnemyTargetJob: ShouldRetryPartialPath 호출 2곳 제거 + 관련 주석 정리
- [x] EnemyTargetJob: AggroLock 섹션 동일 처리
→ 상세: [phase-1-partial-path-reval.md](./phase-1-partial-path-reval.md)

### Phase 2: 대체 타겟 탐색 + Stuck fallback
- [x] EnemyState: AbandonedTarget, AbandonedExpireTime 필드 추가
- [x] EnemyAuthoring Baker 초기화
- [x] GameSettings/GameSettingsAuthoring: TargetAbandonDuration 필드 추가
- [x] EnemyTargetJob: IsPathPartial && Chasing 시 프레임 분산 대체 타겟 탐색
- [x] EnemyTargetJob: Chasing stuck 감지 → AbandonedTarget 설정 + Wandering 전환
- [x] EnemyTargetJob: 타겟 탐색에 AbandonedTarget 필터
- [x] TargetPropagationJob: AbandonedTarget 전파 차단
→ 상세: [phase-2-alt-search-stuck.md](./phase-2-alt-search-stuck.md)

## Phase 간 의존성

| Phase | 의존성 | 병렬 가능 |
|-------|--------|-----------|
| 1 | 없음 | - |
| 2 | Phase 1 | X |

## 변경 파일 요약

| Phase | 파일 | 변경 |
|-------|------|------|
| 1 | `Assets/Scripts/Server/Systems/Movement/FlowFieldSystem.cs` | Apply: IsPathPartial 클리어 추가 |
| 1 | `Assets/Scripts/Server/Systems/Combat/UnifiedTargetingSystem.cs` | dest 억제 제거, ShouldRetryPartialPath 제거 |
| 2 | `Assets/Scripts/Shared/Components/State/EnemyState.cs` | AbandonedTarget, AbandonedExpireTime 추가 |
| 2 | `Assets/Scripts/Authoring/Entities/EnemyAuthoring.cs` | Baker 초기화 |
| 2 | `Assets/Scripts/Shared/Singletons/GameSettings.cs` | TargetAbandonDuration 추가 |
| 2 | `Assets/Scripts/Authoring/Settings/GameSettingsAuthoring.cs` | 인스펙터 필드 + Baker |
| 2 | `Assets/Scripts/Server/Systems/Combat/UnifiedTargetingSystem.cs` | 대체 탐색 + stuck 감지 + AbandonedTarget 필터 |

## 검증 방법

1. EnemyBig → 벽 안 타겟 → 벽 경계 도달 → 정상 대기
2. 타겟이 벽 밖으로 이동 → **즉시** 추격 재개 (2초 대기 없음)
3. 다른 유저 유닛 접근 → 4프레임 내 대체 타겟 전환
4. 대체 타겟 없음 + ~6초 stuck → AbandonedTarget 차단 + Wandering
5. 30초 후 AbandonedTarget 만료 → 재획득 가능
6. 벽 파괴 → GridObstacleCleanupSystem 경유 → 즉시 추격

## 롤백 전략

- Phase 1: FlowFieldSystem + UnifiedTargetingSystem 변경만 git revert
- Phase 2: 컴포넌트 필드 추가는 기존 로직 영향 없음 (기본값 0/Null), 로직은 Phase 1과 함께 revert
