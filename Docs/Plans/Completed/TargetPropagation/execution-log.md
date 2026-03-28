# 실행 기록

## 계획 수정 - 2026-03-28

### 수정 사유
- 4000 적 배회 시 Steering 회피 연산 병목 + 떨림 문제 추가 발견

### 수정 내역
| 항목 | 변경 전 | 변경 후 |
|---|---|---|
| 문제 정의 | 타겟 탐색 병목만 | 타겟 탐색 + Steering 회피 병목 |
| Phase 2 | (없음) | Steering TimeSlice + 캐시 방향 신규 추가 |

### 영향받는 Phase
- Phase 1: 변경 없음
- Phase 2: 신규 추가

---

## Phase 1: TargetPropagationJob 구현 - 2026-03-28

### 실행 내역
| 작업 | 결과 | 비고 |
|------|------|------|
| TargetPropagationJob 구현 | 완료 | IJobEntity, EnemyTag 필터, 3x3 MovementMap 탐색 |
| OnUpdate 스케줄링 변경 | 완료 | handle1 → handle2(propagation) → unitAutoTarget |
| GameSettings 파라미터 | 완료 | TargetPropagationRadius=9.0f |

### 변경된 파일
- `Assets/Scripts/Server/Systems/Combat/UnifiedTargetingSystem.cs` — TargetPropagationJob 추가, 3패스 스케줄링
- `Assets/Scripts/Shared/Singletons/GameSettings.cs` — TargetPropagationRadius 추가
- `Assets/Scripts/Authoring/Settings/GameSettingsAuthoring.cs` — 대응 필드 + Baker 매핑

### 발견된 이슈
- 없음

### Phase 1 완료 판정: Pass

---

## Phase 2: Steering TimeSlice + 캐시 방향 - 2026-03-28

### 실행 내역
| 작업 | 결과 | 비고 |
|------|------|------|
| CachedAvoidanceDir 컴포넌트 생성 | 완료 | float3 Direction + float Strength |
| MovementAuthoring Baker 추가 | 완료 | CachedAvoidanceDir 부착 |
| PredictedMovementSystem TimeSlice | 완료 | entityIndex % Divisor 분산 + 캐시 재사용 |
| GameSettings 파라미터 | 완료 | SteeringSliceDivisor=4 |

### 변경된 파일
- `Assets/Scripts/Shared/Components/Movement/CachedAvoidanceDir.cs` — 신규
- `Assets/Scripts/Authoring/Movement/MovementAuthoring.cs` — Baker에 CachedAvoidanceDir 추가
- `Assets/Scripts/Server/Systems/Movement/PredictedMovementSystem.cs` — TimeSlice + 캐시 로직
- `Assets/Scripts/Shared/Singletons/GameSettings.cs` — SteeringSliceDivisor 추가
- `Assets/Scripts/Authoring/Settings/GameSettingsAuthoring.cs` — 대응 필드 + Baker

### 발견된 이슈
- 없음

### Phase 2 완료 판정: Pass
