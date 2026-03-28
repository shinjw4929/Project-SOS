# 적 AI 버그 수정 오케스트레이션 플랜

## 문제 정의

2가지 적 AI 버그가 게임 플레이를 심각하게 저해하고 있다.

### 문제 1: EnemyBig이 벽 안 타겟을 찾지 못함
- EnemyBig(PathfindingSize=1, Large passability)이 벽으로 둘러싸인 타겟을 발견해도 접근하지 못함
- `FindNearestPassableCell` 탐색 반경이 10셀로 고정 → 벽 너머 passable 셀을 못 찾음
- `FlowFieldCore.ComputeField`가 blocked 목적지에서 즉시 return → 전체 필드 DirNone(255)
- 결과: 무한 IsPathDirty 루프 (매 프레임 재경로 요청 → 실패 반복)

### 문제 2: Wandering 적이 목적지 도착 후 영원히 정지
- 적이 배회 목적지에 도착하면 `MovementArrivalSystem`이 `MovementWaypoints` disable
- `FlowFieldSystem` Wandering 바이패스가 새 waypoint 설정 + MovementWaypoints enable
- **그러나** `FlowFieldSteeringSystem`이 이전 Chasing 상태의 `FlowFieldRef.Key`로 waypoints를 덮어씀
- Key가 여전히 캐시에 존재하면 → 이전 flow field 방향(DirNone 포함)으로 덮어쓰기 → 이동 불가
- Key가 캐시에서 제거되었으면 → IsPathDirty = true → Wandering 바이패스가 다시 설정 → 또 덮어쓰기 → 무한 루프

### 영향 범위
- **Server**: FlowFieldSystem, FlowFieldSteeringSystem, FlowFieldCore
- **Shared**: FlowFieldRef
- **Client**: 직접 변경 없음 (서버 권위 모델)

---

## AS-IS (현재 상태)

### EnemyBig 경로탐색 실패 흐름
1. `UnifiedTargetingSystem.EnemyTargetJob`: 타겟 발견 → `goal.Destination = targetPos`, `IsPathDirty = true`
2. `FlowFieldSystem.OnUpdate()`: destination cell이 Large passability map에서 blocked
3. `FindNearestPassableCell(destCell, passMap, ...)`: 반경 1~10 링 탐색 → 실패 시 `(-1, -1)` 반환
4. 실패 시 destCell 변경 없음 → blocked 목적지로 BFS 실행
5. `FlowFieldCore.ComputeField()`: `passabilityMap[destIndex] != 0` → 즉시 return (전체 필드 255)
6. `ApplyFlowFieldResults()`: 현재 셀 DirNone → 8방향 탐색도 실패 → waypoints disable
7. `FlowFieldSteeringSystem`: DirNone 감지 → `IsPathDirty = true` → 2단계로 돌아가 무한 반복

### Wandering 정지 흐름
1. `MovementArrivalSystem`: 배회 목적지 도착 → `MovementWaypoints` disable, velocity = 0
2. `UnifiedTargetingSystem.EnemyTargetJob`: 새 배회 목적지 생성, `IsPathDirty = true`, `waypointsEnabled = true`
3. `FlowFieldSystem` Wandering 바이패스: `waypoints.Current = destination`, `IsPathDirty = false`, enable
4. `FlowFieldSteeringSystem`: `FlowFieldRef.Key != -1` (이전 Chasing 키) → 캐시 조회 → 이전 flow field 방향으로 `waypoints.Current` 덮어쓰기
5. 덮어쓴 방향이 DirNone이면 → `IsPathDirty = true` → 3~4 반복 (적은 제자리)

### 관련 파일
| 파일 | 역할 | 문제 |
|------|------|------|
| `Server/Systems/Movement/FlowFieldSystem.cs:447-472` | FindNearestPassableCell | 반경 10 고정, 실패 처리 미흡 |
| `Server/Systems/Movement/FlowFieldSystem.cs:220-229` | 목적지 조정 로직 | 조정 실패 시 blocked 목적지 그대로 BFS |
| `Shared/Utilities/FlowFieldCore.cs:78-79` | BFS 엔트리 | blocked 목적지 즉시 return |
| `Server/Systems/Movement/FlowFieldSteeringSystem.cs:38-48` | Steering 쿼리 | Wandering 적 필터링 없음 |
| `Server/Systems/Movement/FlowFieldSteeringSystem.cs:50-54` | Key 체크 | Key == -1만 skip, 유효하지 않은 키 처리 없음 |

---

## TO-BE (목표 상태)

### 문제 1 수정: EnemyBig 경로탐색
- `FindNearestPassableCell` 탐색 반경 10 → 30 확대 (그리드 크기 대비 충분한 범위)
- 탐색 실패 시 (반경 30 내에도 없음) → `IsPathPartial = true` + waypoints disable → 무한 루프 차단
- FlowFieldSystem에서 탐색 실패 처리: `IsPathDirty = false`로 설정하여 재요청 방지, `IsPathPartial = true`로 표기

### 문제 2 수정: Wandering 정지
- FlowFieldSystem Wandering 바이패스에서 `FlowFieldRef.Key = -1` 리셋 추가
- FlowFieldSteeringSystem이 Key == -1인 엔티티를 skip하므로, Wandering 적의 waypoints 덮어쓰기 방지
- 이후 Chasing으로 전환 시 FlowFieldSystem이 새 키를 할당

---

## AS-IS vs TO-BE 비교표

| 항목 | AS-IS | TO-BE |
|------|-------|-------|
| FindNearestPassableCell 반경 | 10 (고정) | 30 (GameSettings.FindNearestPassableCellRadius) |
| 탐색 실패 시 동작 | blocked 목적지로 BFS → 전체 DirNone → 무한 루프 | IsPathPartial=true, IsPathDirty=false, waypoints disable |
| Wandering 바이패스 FlowFieldRef | 이전 Chasing 키 유지 | Key = -1 리셋 |
| FlowFieldSteeringSystem Wandering 처리 | Key != -1이면 무조건 처리 (덮어쓰기) | Key == -1 → skip (기존 로직) |

---

## Phase 체크리스트

### Phase 1: Wandering 정지 수정 + FindNearestPassableCell 확대
- [x] FlowFieldSystem Wandering 바이패스에서 FlowFieldRef.Key = -1 리셋
- [x] FindNearestPassableCell 반경 10 → 30 확대
- [x] 탐색 실패 시 IsPathDirty=false + IsPathPartial=true + waypoints disable (무한 루프 차단)
- [x] 컴파일 확인
→ 상세: [phase-1-수정.md](./phase-1-수정.md)

## Phase 간 의존성
| Phase | 의존성 | 병렬 가능 |
|---|---|---|
| 1 | 없음 | - |

## 변경 파일 요약
| Phase | 파일 | 변경 |
|---|---|---|
| 1 | `Server/Systems/Movement/FlowFieldSystem.cs` | Wandering 바이패스에 FlowFieldRef.Key 리셋 + FindNearestPassableCell 반경 확대 + 실패 처리 |

## 검증 방법
1. EnemyBig이 벽 안 타겟을 향해 벽 바깥까지 접근하여 대기하는지 확인
2. Wandering 적이 목적지 도착 후 새 배회 목적지로 계속 이동하는지 확인
3. 서버 프레임레이트가 유지되는지 확인 (무한 루프 제거)

## 롤백 전략
- Phase 1: FlowFieldSystem.cs만 원복하면 복구 가능
