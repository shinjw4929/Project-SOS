# Phase 4: Authoring/컴포넌트 정리

---

## 변경 상세

### MovementAuthoring.cs
- `AgentTypeIndex` → `PathfindingSize` (Small/Large)
- `GridPathfindingSize` + `FlowFieldRef(Key=-1)` 베이킹
- `AddBuffer<PathWaypoint>` + `NavMeshAgentConfig` 베이킹 제거
- `CurrentWaypointIndex = 0` 초기화 제거 (MovementGoal에서 필드 자체가 제거되므로)

### 신규 컴포넌트

| 파일 | 타입 | 내용 |
|------|------|------|
| `Shared/Components/Movement/GridPathfindingSize.cs` | IComponentData | `byte CellPadding` (0=Small, 1=Large) |
| `Shared/Components/Movement/FlowFieldRef.cs` | IComponentData | `int Key` (캐시 조회 키, 베이킹 시 -1, 런타임에 설정) |
| `Shared/Components/Data/GridObstacleCleanup.cs` | ICleanupComponentData | `int2 GridPosition`, `int Width`, `int Length` |

### 제거 파일

| 파일 | 이유 |
|------|------|
| `Shared/Components/Movement/NavMeshAgentConfig.cs` | GridPathfindingSize로 대체 |
| `Shared/Buffers/PathWaypoint.cs` | Flow Field 직접 스티어링으로 불필요 |
| `Server/Utilities/NavMeshPathUtils.cs` | Funnel 알고리즘 불필요 |

### 수정 파일

| 파일 | 변경 내용 |
|------|----------|
| `Shared/Components/Data/NavMeshObstacleProxy.cs` | `NavMeshObstacleReference` class 제거, `NeedsNavMeshObstacle` struct 유지 |
| `Shared/Components/Movement/MovementGoal.cs` | `CurrentWaypointIndex`, `TotalWaypoints` 필드 제거. `IsPathDirty`, `IsPathPartial`에 `[MarshalAs(UnmanagedType.U1)]` 추가 (Phase 3 Burst 전환 필수). `[GhostComponent]`는 유지하되 개별 `[GhostField]`가 없으므로 필드 제거가 Ghost 동기화에 영향 없음 |
| `Shared/Systems/Grid/ObstacleGridInitSystem.cs` | NavMesh 참조 제거 (태그 재사용) |
| `Authoring/Entities/StructureAuthoring.cs` | `[Header("NavMeshObstacle용")]` 등 Header/Tooltip 문자열 갱신 |
| `Authoring/Entities/ResourceNodeAuthoring.cs` | NavMesh 관련 주석 갱신 |
| `Shared/Components/Stats/StructureFootprint.cs` | `WorldWidth`/`WorldLength`/`WorldHeight`/`IsCircular`/`WorldRadius` 주석 갱신 ("NavMeshObstacle용" → "GridObstacle 밀어내기용"). 필드 자체는 GridObstacleResponseSystem의 밀어내기 로직에서 계속 사용 |

### CurrentWaypointIndex 참조 제거 (6개 시스템, 7곳)

| 파일 | 변경 |
|------|------|
| `Server/Systems/Combat/MeleeAttackSystem.cs` | `CurrentWaypointIndex = 0` 제거 (148행) |
| `Server/Systems/Combat/RangedAttackSystem.cs` | `CurrentWaypointIndex = 0` 제거 (**2곳**: 183행, 261행 struct 초기화 내) |
| `Server/Systems/Commands/Combat/HandleAttackRequestSystem.cs` | `CurrentWaypointIndex = 0` 제거 (146행) |
| `Server/Systems/Commands/Movement/HandleMoveRequestSystem.cs` | `CurrentWaypointIndex = 0` 제거 (107행) |
| `Server/Systems/Commands/Construction/HandleBuildMoveRequestSystem.cs` | `CurrentWaypointIndex = 0` 제거 (151행) |
| `Server/Systems/Commands/Construction/BuildArrivalSystem.cs` | `CurrentWaypointIndex = 0` 제거 (160행) |

### Attribute 변경 (2개 시스템)

| 파일 | 변경 |
|------|------|
| `Server/Systems/Movement/PredictedMovementSystem.cs` | `[UpdateAfter(PathfindingSystem)]` → `[UpdateAfter(FlowFieldSystem)]` |
| `Server/Systems/Combat/UnifiedTargetingSystem.cs` | `[UpdateBefore(PathfindingSystem)]` → `[UpdateBefore(FlowFieldSystem)]` |

---

## 체크리스트

- [ ] `MovementAuthoring` 베이킹 변경
- [ ] `GridPathfindingSize.cs` 신규 작성
- [ ] `FlowFieldRef.cs` 신규 작성
- [ ] `GridObstacleCleanup.cs` 신규 작성
- [ ] `NavMeshAgentConfig.cs` 삭제
- [ ] `PathWaypoint.cs` 삭제
- [ ] `NavMeshPathUtils.cs` 삭제
- [ ] `NavMeshObstacleProxy.cs`에서 `NavMeshObstacleReference` 제거
- [ ] `MovementGoal.cs`에서 `CurrentWaypointIndex`, `TotalWaypoints` 제거 + `IsPathDirty`/`IsPathPartial`에 `[MarshalAs(UnmanagedType.U1)]` 추가
- [ ] `ObstacleGridInitSystem.cs` NavMesh 참조 제거
- [ ] 6개 시스템 `CurrentWaypointIndex = 0` 제거 (RangedAttackSystem 2곳 주의)
- [ ] 2개 시스템 attribute 변경
- [ ] `StructureAuthoring.cs`, `ResourceNodeAuthoring.cs` Header/주석 갱신
- [ ] `StructureFootprint.cs` 주석 갱신 ("NavMeshObstacle용" → "GridObstacle 밀어내기용")
