# 실행 기록

## Phase 1: 포메이션 검증 로직 추가 - 2026-03-29

### 진단 결과: 원래 계획의 원인 분석이 틀렸음

원래 계획은 `HandleMoveRequestSystem.ApplyFormationOffsets`의 groupCenter/포메이션 오프셋을 원인으로 지목했으나, 체계적 진단으로 **실제 원인은 PredictedMovementSystem의 CachedAvoidanceDir**임을 확인.

### 진단 과정

| 진단 | 결과 | 결론 |
|---|---|---|
| 포메이션 오프셋 비활성화 | 문제 유지 | 포메이션이 원인 아님 |
| FlowFieldSystem DirNone 처리 우회 | 문제 유지 | DirNone 특수 처리가 원인 아님 |
| FlowFieldSystem 방향 완전 우회 | 문제 유지 | FlowField 방향이 원인 아님 |
| FlowFieldSteeringSystem 비활성화 | 문제 유지 | 웨이포인트 덮어쓰기가 원인 아님 |
| Steering avoidance 비활성화 | **문제 해결** | **Steering avoidance가 원인** |
| CachedAvoidance Strength 상한 0.3 | **문제 해결** | **최종 수정 적용** |

### 근본 원인

`PredictedMovementSystem`의 `CachedAvoidanceDir` 시스템:
- `cachedAvoidance.Strength`가 `math.saturate(deviation * 2.0f)` → 최대 **1.0**
- non-steering 프레임에서 Strength=1.0이면 웨이포인트 방향을 **100% 무시**하고 캐시된 회피 방향만 사용
- 건물 주변 밀집 유닛들의 강한 회피 방향이 캐시 → non-steering 프레임에서 증폭 → 맵 끝까지 이동

### 실행 내역
| 작업 | 결과 | 비고 |
|---|---|---|
| CachedAvoidance Strength 상한 제한 | Pass | 1.0 → 0.3 (non-steering 프레임에서 캐시 영향 최대 30%) |
| skipMovement 시 캐시 리셋 | Pass | 새 명령 시 잔류 회피 방향 제거 |

### 변경된 파일
- `Assets/Scripts/Server/Systems/Movement/PredictedMovementSystem.cs`
  - skipMovement 시 cachedAvoidance 리셋 (Direction=zero, Strength=0)
  - cachedAvoidance.Strength 상한 0.3으로 제한

### Phase 1 완료 판정: Pass (원래 계획과 다른 수정이지만 근본 문제 해결)
