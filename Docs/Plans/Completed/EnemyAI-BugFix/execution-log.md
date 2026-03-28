# 실행 기록

## Phase 1: Wandering 정지 수정 + FindNearestPassableCell 확대 - 2026-03-28

### 실행 내역
| 작업 | 결과 | 비고 |
|------|------|------|
| Task 1: Wandering FlowFieldRef.Key=-1 리셋 | 완료 | FlowFieldSteeringSystem 덮어쓰기 방지 |
| Task 2: FindNearestPassableCell 반경 30 | 완료 | 10→30 확대 |
| Task 3: 탐색 실패 무한 루프 차단 | 완료 | IsPathDirty=false + IsPathPartial=true |
| 추가: FindNearestWallEdgeCell | 완료 | BFS 도달 불가 시 벽 경계 유도 |
| 추가: Apply IsPathPartial 조건부 설정 | 완료 | 2프레임 진동 방지 |
| 추가: IsPathPartial dest 리셋 억제 | 완료 | ShouldRetryPartialPath에서만 재시도 |
| 추가: IsPathPartial 클리어 조건 | 완료 | dest≈targetPos일 때만 |

### 변경된 파일
- `Server/Systems/Movement/FlowFieldSystem.cs` — Wandering Key 리셋, FindNearestPassableCell 30, 실패 처리, FindNearestWallEdgeCell, Apply IsPathPartial 조건부
- `Server/Systems/Combat/UnifiedTargetingSystem.cs` — IsPathPartial 리셋 억제, ShouldRetryPartialPath dest=target, IsPathPartial 클리어 조건

### 발견된 이슈
- 블랙리스트 기능 시도 → 벽 근처 직선 이동 불가 + BFS 캐시 문제로 원복
- GameDesign.md Small fallback 설명 삭제

### Phase 1 완료 판정: Pass
