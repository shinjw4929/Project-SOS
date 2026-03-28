# 적 AI 버그 수정 (EnemyBig 경로탐색 + Wandering 정지)

## 수정한 문제

### 1. EnemyBig이 벽 안 타겟을 못 찾음
- **원인**: `FindNearestPassableCell`이 타겟 위치(벽 안)에서 탐색 → 벽 내부 passable 셀을 찾음 → BFS가 벽 안쪽에서만 전파 → 외부 엔티티 도달 불가(DirNone) → 무한 IsPathDirty 루프
- **수정**:
  - `FindNearestPassableCell` 탐색 반경 10 → 30 확대
  - 탐색 실패 시 `IsPathDirty=false` + `IsPathPartial=true`로 무한 루프 차단
  - BFS 도달 불가 시 `FindNearestWallEdgeCell`로 엔티티 위치에서 벽 경계 셀 탐색 → 벽 바깥까지 유도

### 2. Wandering 적이 목적지 도착 후 영원히 정지
- **원인**: Wandering 바이패스가 waypoints 설정 후, `FlowFieldSteeringSystem`이 이전 Chasing 상태의 `FlowFieldRef.Key`로 waypoints를 덮어씀
- **수정**: Wandering 바이패스에서 `FlowFieldRef.Key = -1` 리셋 → FlowFieldSteeringSystem이 Key==-1 skip

### 3. 벽 접근 시 급격한 속도 저하 (2프레임 진동)
- **원인**: Apply 성공 시 `IsPathPartial=false` 무조건 클리어 → EnemyTargetJob이 dest를 타겟으로 리셋 → 다시 BFS 실패 → 매 2프레임마다 방향 변경 → 실질 속도 1/2
- **수정**: Apply에서 `IsDestAdjusted==1`일 때만 `IsPathPartial=true` 설정, 기존 true는 유지

### 4. 벽 밖에서 영원히 대기
- **원인**: `IsPathPartial`이면 EnemyTargetJob이 dest 리셋을 억제하므로, 적이 벽 경계에서 무한 대기
- **수정**: `IsPathPartial` + `CheckStuck` (~6초 이동 없음) → 타겟 포기 + Wandering 전환 → 배회 후 재탐색 (자연스러운 순찰 패턴)

## 변경 파일

| 파일 | 변경 내용 |
|------|-----------|
| `Server/Systems/Movement/FlowFieldSystem.cs` | Wandering FlowFieldRef 리셋, FindNearestPassableCell 반경 확대, 탐색 실패 처리, FindNearestWallEdgeCell 신규, Apply IsPathPartial 조건부 설정 |
| `Server/Systems/Combat/UnifiedTargetingSystem.cs` | IsPathPartial dest 리셋 억제, ShouldRetryPartialPath dest=target 리셋, IsPathPartial 클리어 조건, Chasing stuck→Wandering 전환 |
