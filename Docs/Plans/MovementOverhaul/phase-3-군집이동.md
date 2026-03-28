# Phase 3: 군집 이동 (Group Formation)

## 목표
- 동일 목적지로 이동하는 다수 유닛이 격자 대형으로 정렬 이동
- 도착 지점도 유닛별 오프셋이 적용된 개별 좌표
- 이동 중 겹침/비빔 최소화

## 선행 조건
- Phase 1 완료 (FlowField 안정화)
- Phase 2 완료 권장 (Steering 회피가 대형 유지에 도움)

## 설계

### 대형 계산 방식
1. 클라이언트가 이동 명령 전송 시, 선택된 유닛 목록과 목적지를 함께 전달
2. 서버 `HandleMoveRequestSystem`에서 **동일 프레임 + 동일 소유자 + 동일 목적지** 유닛을 그룹으로 묶음
3. 그룹 내 유닛 수 N → √N × √N 격자 계산
4. 각 유닛에 격자 내 슬롯 인덱스 부여 → 오프셋 계산
5. `MovementGoal.Destination += offset` (유닛별 고유 도착 지점)

### 격자 오프셋 계산
```
columns = ceil(sqrt(N))
rows = ceil(N / columns)
spacing = max(ObstacleRadius * 2.5, 1.5)  // 유닛 간 간격

for i in 0..N-1:
    col = i % columns
    row = i / columns
    offset.x = (col - (columns-1)/2.0) * spacing
    offset.z = (row - (rows-1)/2.0) * spacing

// 이동 방향에 맞게 offset 회전
moveDir = normalize(destination - groupCenter)
rotatedOffset = rotate(offset, moveDir)
finalDestination = destination + rotatedOffset
```

### RPC 변경
- 현재: 유닛별 개별 `MoveRequestRpc` 전송
- 변경: 그룹 단위 `GroupMoveRequestRpc` 추가 (UnitGhostIds 배열 + TargetPosition + IsAttackMove)
- 또는 기존 RPC 유지하고 서버에서 동일 프레임 동일 목적지 자동 그룹핑 (RPC 변경 불필요)

## 작업 목록

### Task 1: 서버 그룹 감지 + 오프셋 계산
- [ ] `HandleMoveRequestSystem` 수정:
  - 동일 프레임 RPC 수집 후 (sourceConnection, TargetPosition) 기준 그룹핑
  - NativeMultiHashMap<int, Entity> 활용 (key = position hash + ownerId)
  - 그룹별 격자 오프셋 계산
  - 각 유닛의 `MovementGoal.Destination`에 오프셋 적용
- [ ] `FormationUtility.cs` 신규: 격자 슬롯 계산, 방향 회전 유틸리티
  - `CalculateFormationOffset(int slotIndex, int totalCount, float spacing, float3 moveDir)` → float3 offset
  - Burst 호환 (primitive 파라미터)

### Task 2: 도착 지점 오프셋 적용
- [ ] 각 유닛의 도착 판정은 오프셋 적용된 개별 `Destination` 기준으로 수행 (기존 로직 변경 불필요)
- [ ] `MovementArrivalSystem`은 이미 `MovementGoal.Destination` 기준으로 도착 판정하므로 자연스럽게 대응

### Task 3: 클라이언트 RPC 수정 (선택)
- [ ] 현재 `UnitCommandInputSystem`에서 유닛별 개별 RPC 전송
- [ ] 옵션 A: 그대로 유지 (서버에서 자동 그룹핑) — RPC 변경 불필요
- [ ] 옵션 B: `GroupMoveRequestRpc` 도입 — 네트워크 효율 개선, Ghost 동기화 고려 필요
- [ ] **Phase 3에서는 옵션 A 채택** (최소 변경)

## 병렬 작업 구성

| Agent | 작업 내용 | 의존성 |
|-------|----------|--------|
| Agent A | Task 1 (그룹 감지 + 오프셋) | 없음 |
| Agent B | FormationUtility.cs 신규 + EditMode Test | 없음 |
| Main | Task 2, 3 통합 | Agent A, B 완료 후 |

## 테스트 요구사항

### EditMode Test
- `FormationUtility.CalculateFormationOffset` 단위 테스트:
  - 1유닛: offset = (0,0,0)
  - 4유닛: 2x2 격자 정상 배치
  - 9유닛: 3x3 격자 정상 배치
  - 이동 방향 회전 적용 확인
- 경계 케이스: 유닛 수 1, 2, 3, 5, 10, 50

### PlayMode Test
- 10유닛 동시 이동 → 도착 시 격자 배치 확인
- 30유닛 동시 이동 → 이동 중 심한 겹침 없음 확인

## 검증 방법
1. 10유닛 이동 명령 → 도착 후 유닛 간 최소 거리 > ObstacleRadius * 2
2. 이동 중 시각적으로 대형 유지 확인 (주관적)
3. 기존 단일 유닛 이동 회귀 없음

## 완료 기준
- [ ] 컴파일 성공
- [ ] EditMode Test 통과 (FormationUtility)
- [ ] 다수 유닛 동시 이동 시 격자 대형
- [ ] 단일 유닛 이동은 기존과 동일
