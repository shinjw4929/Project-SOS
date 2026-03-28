# Phase 1: TargetPropagationJob 구현

## 목표
- EnemyTargetJob 완료 후, 타겟 없는 적이 주변 적의 타겟을 복사하여 탐색 비용 절감

## 선행 조건
- 없음

## 작업 목록

### Task 1: TargetPropagationJob 구현
- [ ] `UnifiedTargetingSystem.cs`에 `TargetPropagationJob` IJobEntity 추가:
  ```csharp
  [BurstCompile]
  [WithAll(typeof(EnemyTag))]
  public partial struct TargetPropagationJob : IJobEntity
  {
      [ReadOnly] public NativeParallelMultiHashMap<int, SpatialMovementEntry> MovementMap;
      [ReadOnly] public ComponentLookup<AggroTarget> AggroTargetLookup;
      [ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;
      [ReadOnly] public ComponentLookup<Health> HealthLookup;
      [ReadOnly] public ComponentLookup<EnemyTag> EnemyTagLookup;
      public float CellSize;
      public float PropagationRadius;

      void Execute(
          Entity entity,
          ref AggroTarget target,
          ref EnemyState enemyState,
          ref MovementGoal goal,
          EnabledRefRW<MovementWaypoints> waypointsEnabled,
          in LocalTransform transform)
      {
          // 이미 타겟 있으면 skip
          if (target.TargetEntity != Entity.Null) return;
          // Dormant/Dying/Dead는 skip
          if (enemyState.CurrentState == EnemyContext.Dormant ||
              enemyState.CurrentState == EnemyContext.Dying ||
              enemyState.CurrentState == EnemyContext.Dead) return;

          float3 myPos = transform.Position;
          float bestDistSq = PropagationRadius * PropagationRadius;
          Entity bestTarget = Entity.Null;
          float3 bestTargetPos = float3.zero;

          // 주변 적 탐색 (MovementMap 3x3)
          for (int x = -1; x <= 1; x++)
          {
              for (int z = -1; z <= 1; z++)
              {
                  int hash = SpatialHashUtility.GetCellHash(myPos, x, z, CellSize);
                  if (!MovementMap.TryGetFirstValue(hash, out var neighbor, out var it))
                      continue;
                  do
                  {
                      if (neighbor.Entity == entity) continue;
                      if (!EnemyTagLookup.HasComponent(neighbor.Entity)) continue;
                      if (!AggroTargetLookup.TryGetComponent(neighbor.Entity, out AggroTarget neighborTarget))
                          continue;
                      if (neighborTarget.TargetEntity == Entity.Null) continue;

                      // 이웃의 타겟이 살아있는지 확인
                      if (!HealthLookup.TryGetComponent(neighborTarget.TargetEntity, out Health h)
                          || h.CurrentValue <= 0) continue;

                      // 이웃과의 거리 확인
                      if (!TransformLookup.TryGetComponent(neighbor.Entity, out LocalTransform neighborTransform))
                          continue;
                      float distSq = math.distancesq(myPos, neighborTransform.Position);
                      if (distSq >= bestDistSq) continue;

                      // 타겟 위치 확인
                      if (!TransformLookup.TryGetComponent(neighborTarget.TargetEntity, out LocalTransform targetTransform))
                          continue;

                      bestDistSq = distSq;
                      bestTarget = neighborTarget.TargetEntity;
                      bestTargetPos = targetTransform.Position;

                  } while (MovementMap.TryGetNextValue(out neighbor, ref it));
              }
          }

          if (bestTarget == Entity.Null) return;

          // 타겟 전파
          target.TargetEntity = bestTarget;
          target.LastTargetPosition = bestTargetPos;
          enemyState.CurrentState = EnemyContext.Chasing;
          goal.Destination = bestTargetPos;
          goal.IsPathDirty = true;
          waypointsEnabled.ValueRW = true;
      }
  }
  ```

### Task 2: OnUpdate 스케줄링 변경
- [ ] `UnifiedTargetingSystem.OnUpdate`에서 EnemyTargetJob 후 TargetPropagationJob 삽입:
  ```csharp
  // 기존
  var handle1 = enemyTargetJob.ScheduleParallel(state.Dependency);
  state.Dependency = unitAutoTargetJob.ScheduleParallel(handle1);

  // 변경
  var handle1 = enemyTargetJob.ScheduleParallel(state.Dependency);
  var handle2 = propagationJob.ScheduleParallel(handle1);
  state.Dependency = unitAutoTargetJob.ScheduleParallel(handle2);
  ```
- [ ] TargetPropagationJob에 필요한 Lookup/Map 전달:
  - `MovementMap`: SpatialMaps 싱글톤에서 획득 (이미 OnUpdate 초반에 접근)
  - `AggroTargetLookup`: `[ReadOnly] [NativeDisableContainerSafetyRestriction]` 사용. Job 내에서 타겟 있는 적은 읽기만, 타겟 없는 적은 Execute 파라미터로 쓰기 → 동일 엔티티 동시 읽기+쓰기 없음. Pass 1 완료 후 실행이므로 안전.

### Task 3: GameSettings 파라미터
- [ ] `GameSettings`에 `TargetPropagationRadius` 추가 (기본값: 9.0f = MovementCellSize * 3)
- [ ] `GameSettingsAuthoring`에 대응 필드 추가
- [ ] Baker 매핑 추가

## 병렬 작업 구성

| Agent | 작업 내용 | 의존성 |
|-------|----------|--------|
| Main | Task 1 → Task 2 → Task 3 (순차, 동일 파일) | 없음 |

## 테스트 요구사항

### PlayMode Test
- 적 100마리 + 아군 1개 → 전파 확인 (1초 내 대부분 Chasing)
- 적 4000마리 → Profiler 비교 (기존 vs 전파 적용)

## 검증 방법
1. 적 스폰 후 1마리 발견 → 3초 내 반경 30m 적 80%가 Chasing
2. Profiler: UnifiedTargetingSystem 전체 < 기존 대비 50% 이하
3. 기존 전투 동작 회귀 없음

## 완료 기준
- [ ] 컴파일 성공
- [ ] 전파 동작 확인
- [ ] 성능 개선 확인 (Profiler)
- [ ] 기존 전투 회귀 없음
