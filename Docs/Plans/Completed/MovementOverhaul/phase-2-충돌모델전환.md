# Phase 2: 충돌 모델 전환 (Separation 제거 → Steering 회피)

## 목표
- Separation force **완전 제거** (유닛/적 간 밀림 원천 차단)
- **Steering 기반 회피**: 이동 방향만 조정, 위치 직접 변경 없음
- 벽 절대 투과 불가 보장
- `MovementArrivalSystem` 2차 판정의 Separation 의존성 제거

## 선행 조건
- 없음 (Phase 1과 병렬 가능)

## 설계 원칙

### 기획 요구사항
> 유닛(적) <-> 유닛(적) 끼리 힘이 작용하면 서로 밀리지 않아야한다. 필요 시 비켜간다.

### Separation 제거가 영향을 주는 시스템
1. **PredictedMovementSystem**: `CalculateSeparation` 호출 → 제거
2. **MovementArrivalSystem**: 2차 판정이 Separation 진동을 감지 → 대체 로직 필요
3. **FlowFieldSteeringSystem**: 주석에서 Separation 언급 → 주석만 수정

### MovementArrivalSystem 2차 판정 대체
현재 2차 판정: "확장 반경(2배) 이내 + 목적지 반대 방향 이동 → 도착 처리"
- **원래 목적**: Separation force가 도착 반경 진입을 방해하여 진동하는 유닛 포착
- **Separation 제거 후**: 진동 원인 자체가 사라짐 → 2차 판정 불필요
- **안전망**: Steering 회피로 인한 미세 진동 대비, **정체 시간 기반 도착 판정** 추가
  - 확장 반경(2배) 이내에서 N초(0.5초) 이상 체류하면 도착 처리
  - 또는 속도가 매우 낮으면(< 0.1 m/s) 도착 처리

### Steering 회피 알고리즘
```
이동 중인 엔티티 A가 이웃 B와 충돌 예상 시:
1. A의 desiredVelocity 방향을 B의 반대쪽으로 편향 (회전)
2. B가 정지 중이면 B를 장애물로 취급하고 우회
3. B도 이동 중이면 상호 회피:
   - entityIndex가 작은 쪽이 오른쪽, 큰 쪽이 왼쪽으로 편향 (결정론적)
4. 편향 강도: 거리 가까울수록 강한 회전
5. 위치를 직접 변경하지 않음 → 밀림 없음
6. skipMovement=true(정지/공격 중)이면 Steering도 skip → 제자리 유지
```

## 작업 목록

### Task 1: CalculateSeparation → CalculateSteeringAvoidance 교체
- [ ] `PredictedMovementSystem.KinematicMovementJob`:
  - `CalculateSeparation` 메서드 삭제
  - `CalculateSteeringAvoidance` 메서드 신규:
    - 입력: `myPos`, `myRadius`, `desiredVelocity`, `entity`, `iAmEnemy`, `iAmFlying`, `iAmWorking`
    - 출력: `float3 adjustedVelocity` (회전된 속도 벡터)
    - SpatialMap 3x3 이웃 탐색은 동일 패턴 유지
  - 적용 변경:
    ```csharp
    // AS-IS
    float3 separationForce = CalculateSeparation(...);
    float3 finalVelocity = desiredVelocity + (separationForce * SeparationStrength);

    // TO-BE
    float3 finalVelocity = skipMovement ? float3.zero
        : CalculateSteeringAvoidance(currentPos, obstacleRadius.Radius,
            desiredVelocity, entity, iAmEnemy, iAmFlying, iAmWorking);
    ```
  - `skipMovement=true` 시: `finalVelocity = float3.zero` (정지 유닛은 제자리)
  - **주의**: 현재 Separation은 skipMovement에서도 실행되어 공격 중 유닛이 밀림. Steering 제거 후 정지 유닛은 완전 고정됨. 기획 의도("밀리면 안 됨")와 일치하므로 의도적 동작 변경.
- [ ] Steering 회피 로직 구현:
  ```csharp
  float3 CalculateSteeringAvoidance(
      float3 myPos, float myRadius, float3 desiredVelocity,
      Entity myEntity, bool iAmEnemy, bool iAmFlying, bool iAmWorking)
  {
      if (math.lengthsq(desiredVelocity) < 0.001f) return desiredVelocity;

      float3 adjustedDir = math.normalizesafe(desiredVelocity);
      float desiredSpeed = math.length(desiredVelocity);
      float avoidWeight = 0f;
      float3 avoidDir = float3.zero;

      for (x,z in [-1,1]):
          hash = GetCellHash(myPos, x, z, CellSize)
          for each neighbor in SpatialMap[hash]:
              if (neighbor == self) continue;
              if (iAmFlying != neighborIsFlying) continue;
              // shouldCollide 체크 동일

              float3 toOther = neighborPos - myPos;
              toOther.y = 0;
              float dist = math.length(toOther);
              float combinedRadius = myRadius + otherRadius + AvoidancePadding;

              if (dist >= combinedRadius || dist < 0.001f) continue;

              // 충돌 중 또는 임박: 회피 방향 계산
              float3 awayDir = math.normalizesafe(myPos - neighborPos);
              // 결정론적 좌/우 분산 (entityIndex 비교)
              float3 perpDir = math.cross(awayDir, math.up());
              if (myEntity.Index < neighbor.Entity.Index)
                  perpDir = -perpDir;

              float overlap = 1.0f - (dist / combinedRadius);
              avoidDir += math.normalizesafe(awayDir + perpDir) * overlap;
              avoidWeight += overlap;

      if (avoidWeight > 0.001f)
      {
          avoidDir = math.normalizesafe(avoidDir);
          float blendFactor = math.saturate(avoidWeight * AvoidanceStrength);
          adjustedDir = math.normalizesafe(math.lerp(adjustedDir, avoidDir, blendFactor));
      }

      return adjustedDir * desiredSpeed;
  }
  ```

### Task 2: MovementArrivalSystem 2차 판정 수정
- [ ] 기존 Separation 기반 2차 판정 제거
- [ ] 대체: 확장 반경 내에서 속도가 매우 낮으면 도착 처리
  ```csharp
  // AS-IS (Separation 역방향 감지)
  float expandedRadiusSq = arrivalRadiusSq * 4f;
  if (distanceSq < expandedRadiusSq)
  {
      if (math.dot(velocity.Linear, toTarget) <= 0)
          return true;
  }

  // TO-BE (저속 감지)
  float expandedRadiusSq = arrivalRadiusSq * 4f;
  if (distanceSq < expandedRadiusSq)
  {
      if (math.lengthsq(velocity.Linear) < 0.01f) // 거의 정지
          return true;
  }
  ```

### Task 3: GameSettings 파라미터 변경
- [ ] 제거: `SeparationStrength`, `SeparationPadding`, `SeparationForceCurve`
- [ ] 추가: `AvoidanceStrength` (회피 블렌딩 강도, 기본 2.0), `AvoidancePadding` (여유 거리, 기본 0.3)
- [ ] `GameSettingsAuthoring` 대응 변경
- [ ] `PredictedMovementSystem.OnUpdate`에서 새 파라미터 읽어서 Job 전달

### Task 4: 벽 투과 방지 강화
- [ ] 이동 적용 순서 변경:
  ```csharp
  float3 newPos = transform.Position + finalVelocity * DeltaTime;
  if (!iAmFlying && IsOverlappingBlockedCell(newPos, obstacleRadius.Radius))
  {
      // 축별 분리 시도
      float3 xOnly = transform.Position + new float3(finalVelocity.x * DeltaTime, 0, 0);
      float3 zOnly = transform.Position + new float3(0, 0, finalVelocity.z * DeltaTime);
      if (!IsOverlappingBlockedCell(xOnly, obstacleRadius.Radius))
          newPos = xOnly;
      else if (!IsOverlappingBlockedCell(zOnly, obstacleRadius.Radius))
          newPos = zOnly;
      else
          newPos = transform.Position; // 둘 다 blocked → 정지
  }
  transform.Position = newPos;
  if (!iAmFlying) ClampToWall(ref transform.Position, obstacleRadius.Radius);
  ```
- [ ] `ClampToWall` 반복 횟수 3 → 5
- [ ] `ResolveWallCollision`은 Steering 회피와 중복 → 제거 또는 간소화

### Task 5: 주석, 문서, 씬 데이터 정리
- [ ] `FlowFieldSteeringSystem`: Separation 관련 주석 수정
- [ ] `PredictedMovementSystem`: 기존 Separation 주석 제거/교체
- [ ] `Docs/Architecture.md`: line 262, 271의 Separation 파라미터 참조를 Avoidance로 수정
- [ ] `EntitiesSubScene.unity`: GameSettings 필드 변경으로 씬 리베이크 필요 (Unity Editor에서 GameSettingsAuthoring Inspector 재설정 후 저장)

## 병렬 작업 구성

| Agent | 작업 내용 | 의존성 |
|-------|----------|--------|
| Main | Task 1 → Task 3 → Task 4 → Task 5 (순차, 동일 파일) | 없음 |
| Agent B | Task 2 (MovementArrivalSystem, 별도 파일) | 없음 |

## 테스트 요구사항

### EditMode Test
- Steering 회피 방향 테스트:
  - 정면 이웃: 좌/우 분산 (entityIndex 기반 결정론적)
  - 후방 이웃: 회피 없음 (dist >= combinedRadius)
  - 정지 이웃: 우회 방향 생성

### PlayMode Test
- 유닛 10개 좁은 통로 이동 → 벽 투과 0건
- 유닛 20개 같은 지점 이동 → 밀림 없음 (정지 유닛 위치 불변)
- 적 50마리 벽 앞 밀집 → 벽 투과 0건, 끼임 없음

## 검증 방법
1. 벽 투과: 유닛 20개 벽 방향 이동 → 투과 0건
2. 밀림: 정지 유닛 옆에 이동 유닛 → 정지 유닛 위치 불변
3. 회피: 이동 유닛이 정지 유닛을 비켜감
4. 도착: 유닛이 목적지 근처에서 정상 정지 (무한 진동 없음)
5. 성능: PredictedMovementSystem 프레임 시간 기존 대비 ±20%

## 완료 기준
- [ ] 컴파일 성공
- [ ] 벽 투과 0건
- [ ] 유닛 간 밀림 없음
- [ ] 이동 유닛이 장애물을 비켜감
- [ ] 도착 판정 정상 (진동 없음)
- [ ] 기존 전투/채집 동작 회귀 없음
