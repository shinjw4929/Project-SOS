# Phase 2: Steering TimeSlice + 캐시 방향

## 목표
- Steering 회피 계산을 TimeSlice하여 연산량 4배 감소
- 캐시된 방향을 재사용하여 떨림 방지
- 겹침 없음 + 밀림 없음 원칙 유지

## 선행 조건
- 없음 (Phase 1과 병렬 가능)

## 설계

### CachedAvoidanceDir 컴포넌트
- `float3 Value`: 마지막으로 계산된 회피 방향 (정규화)
- `float Strength`: 회피 블렌딩 강도 (0~1, 0이면 회피 없음)
- 초기값: `float3.zero`, `0f`

### TimeSlice 로직
```
// KinematicMovementJob.Execute 내부
bool isMySteeringFrame = ((uint)entity.Index % SteeringSliceDivisor)
    == (FrameCount % SteeringSliceDivisor);

if (isMySteeringFrame || math.lengthsq(cachedDir.Value) < 0.001f)
{
    // 이웃 탐색 + 회피 방향 계산 (기존 CalculateSteeringAvoidance)
    float3 avoidResult = CalculateSteeringAvoidance(...);
    cachedDir.Value = math.normalizesafe(avoidResult - desiredVelocity);
    cachedDir.Strength = ...; // 계산된 블렌딩 강도
    finalVelocity = avoidResult;
}
else
{
    // 캐시된 방향 재사용
    if (cachedDir.Strength > 0.001f)
    {
        float3 adjustedDir = math.normalizesafe(
            math.lerp(math.normalizesafe(desiredVelocity), cachedDir.Value, cachedDir.Strength));
        finalVelocity = adjustedDir * math.length(desiredVelocity);
    }
    else
    {
        finalVelocity = desiredVelocity;
    }
}
```

### 안전망
- `cachedDir.Value`가 zero이면 (첫 프레임) 즉시 계산 실행
- skipMovement=true 시 캐시도 skip → 정지 유닛 완전 고정

## 작업 목록

### Task 1: CachedAvoidanceDir 컴포넌트 생성
- [x] `Assets/Scripts/Shared/Components/Movement/CachedAvoidanceDir.cs` 신규:
  ```csharp
  public struct CachedAvoidanceDir : IComponentData
  {
      public float3 Direction;
      public float Strength;
  }
  ```
- [x] `MovementAuthoring.cs` Baker에서 `CachedAvoidanceDir` 추가 (초기값 zero)

### Task 2: PredictedMovementSystem TimeSlice 적용
- [x] `KinematicMovementJob`에 필드 추가:
  - `uint FrameCount` (OnUpdate에서 `state.GlobalSystemVersion` 전달)
  - `uint SteeringSliceDivisor` (GameSettings에서 읽기)
- [x] `Execute` 메서드:
  - `CachedAvoidanceDir` ref 파라미터 추가 (EntityQuery 갱신 필요)
  - TimeSlice 조건 체크 → 계산 or 캐시 재사용
- [x] `CalculateSteeringAvoidance` 반환값에서 캐시 데이터 추출

### Task 3: GameSettings 파라미터
- [x] `GameSettings.SteeringSliceDivisor` 추가 (기본: 4)
- [x] `GameSettingsAuthoring` 대응 필드 + Baker 매핑

## 병렬 작업 구성

| Agent | 작업 내용 | 의존성 |
|-------|----------|--------|
| Agent A | Task 1 (컴포넌트 + Authoring) | 없음 |
| Main | Task 2 + Task 3 (PredictedMovementSystem + GameSettings) | Task 1 완료 후 |

## 테스트 요구사항

### PlayMode Test
- 적 4000마리 배회 → Profiler: PredictedMovementSystem 50% 감소
- 적 밀집 → 떨림 없음 확인
- 유닛-적 간 겹침/밀림 없음 유지

## 검증 방법
1. Profiler: PredictedMovementSystem < 기존 대비 50%
2. 적 밀집 시 시각적 떨림 없음
3. 겹침/밀림 없음 유지 (벽 투과 0건)

## 완료 기준
- [ ] 컴파일 성공
- [ ] 성능 개선 확인 (Profiler)
- [ ] 떨림 감소 확인
- [ ] 겹침/밀림 없음 유지
