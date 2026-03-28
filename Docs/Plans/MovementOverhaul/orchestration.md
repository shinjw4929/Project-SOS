# 이동 시스템 대규모 개선 오케스트레이션 플랜

## 문제 정의

4가지 핵심 문제가 이동 시스템의 확장성(4000+ 엔티티)과 게임플레이 품질을 저해하고 있다.

1. **FlowField 병목**: BFS 캐시 32슬롯 한계 + Wandering 적 4000마리가 각자 고유 목적지 → 매 프레임 대량 BFS 재계산 → 서버 15ms+ 블로킹. FlowFieldSteeringSystem이 메인 스레드 foreach로 O(N) 처리.
2. **유닛 군집 이동 부재**: 동일 목적지로 이동하는 다수 유닛이 단일 지점에 수렴 → Separation force 충돌 → 뭉쳐서 비빔. 정렬된 대형으로 이동/도착해야 함.
3. **적 타겟 탐색 실패**: 적이 아군 유닛을 제대로 감지하지 못하고 산발적으로 퍼져나감. TargetingMap 셀 크기(10.0f)와 TimeSliceDivisor(4프레임) 간 상호작용 문제 의심.
4. **벽 투과 및 Separation 밀림**: Separation force가 벽 인식 없이 적용 → 유닛이 벽으로 밀려 투과 → ClampToWall 보정 실패 시 끼임. 기획: 유닛/적 간 서로 밀리면 안 됨, 벽 절대 투과 불가.

### 영향 범위
- **Server**: FlowFieldSystem, FlowFieldSteeringSystem, PredictedMovementSystem, UnifiedTargetingSystem, HandleMoveRequestSystem, MovementArrivalSystem, SpatialMapBuildSystem
- **Shared**: GameSettings, WanderUtility, SpatialHashUtility, MovementGoal, MovementWaypoints
- **Client**: 직접 변경 없음 (서버 권위 모델)

---

## AS-IS (현재 상태)

### FlowField 경로탐색
- `FlowFieldSystem`: Small/Large 각 32슬롯 LRU 캐시, 8 IJob 워커 BFS
- BFS 캐시 miss → `JobHandle.Complete()` 메인 스레드 블로킹 (프로파일러: 15.75ms)
- Wandering 적: `WanderUtility.GenerateWanderDestination()` → 맵 전체 범위 랜덤 좌표 → `IsPathDirty=true` → FlowField BFS 요청
- `FlowFieldSteeringSystem`: 메인 스레드 foreach O(N), IJobEntity 아님

### 군집 이동
- `HandleMoveRequestSystem`: 모든 유닛에 동일한 `MovementGoal.Destination` 설정
- 포메이션/오프셋 로직 없음
- `MovementArrivalSystem`: 모든 유닛이 동일 지점에 도착 시도 → Separation 진동

### 적 타겟 탐색
- `UnifiedTargetingSystem.EnemyTargetJob`: TargetingMap(CellSize=10.0f) 기반 탐색
- `TimeSliceDivisor=4`: 75%의 프레임에서 탐색 건너뜀 → Wandering 전환
- 탐색 반경: `ceil(visionRange * 1.3 / 10.0)` = 2~3셀
- Wandering 목적지: 맵 전체 랜덤 → 아군 기지 방향 편향 없음

### Separation 및 벽 충돌
- `CalculateSeparation`: 이웃 수에 비례 force 누적, 상한 없음
- force 방향이 벽 인식 없음 → `transform.Position += finalVelocity * dt` → 벽 관통
- `ClampToWall`: 3회 반복 보정이지만 고밀도 시 부족
- Separation 설계 의도: 유닛 간 밀어내기 → **기획과 불일치** (밀리면 안 됨)

### 관련 파일
| 파일 | 역할 |
|------|------|
| `Server/Systems/Movement/FlowFieldSystem.cs` | BFS 계산 + LRU 캐시 |
| `Server/Systems/Movement/FlowFieldSteeringSystem.cs` | FlowField 방향 → Waypoint |
| `Server/Systems/Movement/PredictedMovementSystem.cs` | 이동 + Separation + 벽 충돌 |
| `Server/Systems/Movement/MovementArrivalSystem.cs` | 도착 판정 |
| `Server/Systems/Combat/UnifiedTargetingSystem.cs` | 적/유닛 타겟 탐색 + Wandering |
| `Server/Systems/Commands/Movement/HandleMoveRequestSystem.cs` | 이동 명령 처리 |
| `Shared/Utilities/WanderUtility.cs` | 배회 목적지 생성 |
| `Shared/Utilities/SpatialHashUtility.cs` | 공간 분할 해시 |
| `Shared/Singletons/GameSettings.cs` | Separation/Targeting 파라미터 |

---

## TO-BE (목표 상태)

### 1. FlowField 확장 + Wandering 분리
- `MaxFields` 32 → 128 (Small/Large 각각)
- Wandering 적: FlowField BFS 대신 **직선 이동** (Flying과 동일 패턴)
- 프레임당 BFS 계산 수 상한 도입 (캐시 miss 폭주 방지)
- `PredictedMovementSystem` ISystem 레벨 `[BurstCompile]` 누락 수정
- ~~FlowFieldSteeringSystem IJobEntity 전환~~ (검토 결과: 워크로드 미미, foreach 유지)

### 2. 군집 이동 (Group Formation)
- `HandleMoveRequestSystem`: 동일 프레임 동일 목적지 유닛 그룹핑
- 그룹 내 각 유닛에 **격자 오프셋** 적용 (행렬 대형)
- 도착 지점도 오프셋 적용 → Separation 진동 방지
- 구현: RPC에 GroupId 추가 또는 서버에서 동일 프레임 동일 목적지 자동 감지

### 3. 적 타겟 탐색 개선
- Wandering 방향을 **아군 기지 방향으로 편향** (맵 전체 랜덤 → 반경 제한 + 편향)
- TargetSearchInterval 조정 또는 최초 타겟 탐색 시 즉시 검색
- TargetingMap 셀 크기 검토 (10.0f → 8.0f 고려)

### 4. Separation 제거 → Steering 기반 회피 + 벽 투과 방지 강화
- **Separation force 완전 제거** (유닛/적 간 밀림 원천 차단)
- **Steering 기반 회피**: 이동 방향만 조정, 위치 직접 변경 안 함 → 밀림 없음
- **MovementArrivalSystem 2차 판정**: Separation 의존 제거 → 저속 기반 도착 판정으로 대체
- 벽 충돌: 이동 전 검증 + 축별 분리 시도 + `ClampToWall` 반복 5회로 강화

---

## AS-IS vs TO-BE 비교표

| 항목 | AS-IS | TO-BE |
|------|-------|-------|
| FlowField 캐시 | 32슬롯 (Small/Large) | 128슬롯 |
| Wandering 경로 | FlowField BFS (캐시 폭주) | 직선 이동 (BFS 불필요) |
| FlowFieldSteering | 메인 스레드 foreach | 유지 (워크로드 미미, 병렬화 이점 없음) |
| 프레임당 BFS 상한 | 없음 (무제한 계산) | 최대 N개/프레임 |
| 군집 이동 | 단일 지점 수렴 | 격자 대형 오프셋 |
| 도착 지점 | 모든 유닛 동일 좌표 | 유닛별 오프셋 좌표 |
| 적 Wandering 방향 | 맵 전체 랜덤 | 아군 기지 편향 + 반경 제한 |
| 충돌 회피 | Separation force (밀림) | Steering 회피 (방향만 조정, 밀림 없음) |
| 벽 충돌 | Separation 후 ClampToWall 3회 보정 | 이동 전 벽 검증 + 축별 분리 + ClampToWall 5회 |
| 도착 2차 판정 | Separation 역방향 감지 | 저속 기반 감지 (Separation 의존 제거) |

---

## Phase 체크리스트

### Phase 1: FlowField 확장 + Wandering 분리
- [x] `MaxFields` 32 → 128
- [x] Wandering 적 FlowField 바이패스 (직선 이동)
- [x] 프레임당 BFS 상한 도입
- [x] `PredictedMovementSystem` ISystem [BurstCompile] 추가
> 상세: [phase-1-flowfield-확장.md](./phase-1-flowfield-확장.md)

### Phase 2: 충돌 모델 전환 (Separation → Steering)
- [x] Separation 제거 + Steering 회피 구현
- [x] MovementArrivalSystem 2차 판정 수정 (저속 기반)
- [x] 벽 투과 방지 강화 (이동 전 검증 + ClampToWall 5회)
- [x] GameSettings 파라미터 변경 (Separation → Avoidance)
> 상세: [phase-2-충돌모델전환.md](./phase-2-충돌모델전환.md)

### Phase 3: 군집 이동 (Group Formation)
- [x] HandleMoveRequestSystem 그룹 감지 + 오프셋 계산
- [x] 도착 지점 오프셋 적용
- [x] MovementArrivalSystem 오프셋 도착 판정
> 상세: [phase-3-군집이동.md](./phase-3-군집이동.md)

### Phase 4: 적 타겟 탐색 개선
- [x] Wandering 방향 편향 (아군 기지 방향)
- [x] 타겟 탐색 주기/범위 조정
> 상세: [phase-4-타겟탐색개선.md](./phase-4-타겟탐색개선.md)

---

## Phase 간 의존성

| Phase | 의존성 | 병렬 가능 |
|-------|--------|-----------|
| 1 | 없음 | - |
| 2 | 없음 | O (Phase 1과 병렬) |
| 3 | Phase 1 (FlowField 안정화 후) | X |
| 4 | 없음 | O (Phase 1, 2와 병렬) |

---

## 변경 파일 요약

| Phase | 파일 | 변경 |
|-------|------|------|
| 1 | `FlowFieldSystem.cs` | MaxFields 128, Wandering 바이패스, BFS 상한 |
| 1 | `PredictedMovementSystem.cs` | ISystem [BurstCompile] 추가 |
| 2 | `PredictedMovementSystem.cs` | Separation 제거, Steering 회피, 벽 검증 강화 |
| 2 | `MovementArrivalSystem.cs` | 2차 판정 저속 기반으로 변경 |
| 2 | `GameSettings.cs` | Separation 파라미터 → Avoidance 파라미터 |
| 2 | `GameSettingsAuthoring.cs` | 대응 필드 변경 |
| 3 | `HandleMoveRequestSystem.cs` | 그룹 감지 + 오프셋 계산 |
| 3 | `MovementArrivalSystem.cs` | 오프셋 도착 판정 |
| 3 | `MovementGoal.cs` (또는 신규) | FormationOffset 필드 |
| 4 | `WanderUtility.cs` | 편향 방향 생성 |
| 4 | `UnifiedTargetingSystem.cs` | 탐색 주기/범위 조정 |

---

## 검증 방법

1. **Phase 1**: 4000 EnemySmall 스폰 → FlowFieldSystem 프레임 시간 < 2ms 확인 (Profiler)
2. **Phase 2**: 유닛 10개를 벽 근처에서 이동 → 벽 투과 0건, 유닛 간 밀림 없음, 정지 유닛 위치 불변
3. **Phase 3**: 유닛 20개를 동일 지점으로 이동 명령 → 격자 대형 유지, 도착 시 정렬 확인
4. **Phase 4**: 적 100마리 스폰 → 10초 내 아군 유닛 발견 비율 > 80%

---

## 롤백 전략

- **Phase 1**: `MaxFields` 상수 복원, Wandering 바이패스 코드 제거
- **Phase 2**: Steering 코드 제거, Separation 코드 복원 (git revert), MovementArrivalSystem 2차 판정 복원
- **Phase 3**: FormationOffset 필드 제거, HandleMoveRequestSystem 그룹 로직 제거
- **Phase 4**: WanderUtility 편향 로직 제거, 기존 랜덤 복원
