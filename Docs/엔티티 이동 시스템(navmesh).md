# 엔티티 이동 시스템(navmesh)

# 전체 흐름

```
[Client] 우클릭 → MoveRequestRpc
    ↓
[Server] HandleMoveRequestSystem → MovementGoal 설정
    ↓
[SpatialPartitioningGroup]
SpatialMapBuildSystem → MovementMap 빌드 (셀 크기: 3.0f)
    ↓
[SimulationSystemGroup]
PathfindingSystem → NavMesh 경로 계산 → PathWaypoint 버퍼
    ↓
PathFollowSystem → MovementWaypoints.Current/Next 공급
    ↓
PredictedMovementSystem → LocalTransform 직접 이동 (SpatialMaps.MovementMap 사용)
    ↓
MovementArrivalSystem → 도착 판정 → 이동 정지
    ↓
[LateSimulationSystemGroup]
SpatialMapDisposeSystem → MovementMap 해제
```

# 핵심 설계 포인트

1. 서버 권위 모델: 모든 이동 계산은 서버에서 수행, 클라이언트는 Ghost 보간으로 시각화
2. 3계층 구조: MovementGoal(명령) → PathWaypoint(계획) → MovementWaypoints(실행)
3. Kinematic 이동: PhysicsVelocity가 아닌 LocalTransform.Position 직접 수정
4. 대역폭 최적화: 전체 경로 대신 Current/Next 두 지점만 동기화
5. 공간 분할 충돌 회피: SpatialMaps.MovementMap(셀 크기 3.0f)을 사용한 Entity 기반 Separation

## 클라이언트 시스템 (Client/Systems/)

| 파일 | 그룹 | 역할 |
| --- | --- | --- |
| UnitCommandInputSystem.cs | GhostInputSystemGroup | 우클릭 → Physics Raycast → MoveRequestRpc 생성 및 전송 (선택된 모든 유닛 대상) |

## 서버 시스템 (Server/Systems/)

### 공간 분할 시스템 (Server/Systems/Spatial/)

파일: SpatialMapBuildSystem.cs
그룹: SpatialPartitioningGroup (OrderFirst=true)
역할: 공간 분할 맵 빌드
- MovementMap (셀 크기: 3.0f): 이동 충돌 회피용 (대형 유닛 AABB 등록)
- 결과를 SpatialMaps 싱글톤에 저장
────────────────────────────────────────
파일: SpatialMapDisposeSystem.cs
그룹: LateSimulationSystemGroup
역할: 공간 분할 맵 해제. CompleteDependency() 후 맵 Dispose

────────────────────────────────────────

### 이동 시스템 (Server/Systems/Movement/)

파일: HandleMoveRequestSystem.cs
그룹: SimulationSystemGroup
역할: MoveRequestRpc 수신 → MovementGoal.Destination 설정, IsPathDirty=true, UnitIntentState=Move, MovementWaypoints 활성화
────────────────────────────────────────
파일: NavMeshObstacleSpawnSystem.cs
그룹: SimulationSystemGroup
역할: 건물 생성 시 NavMeshObstacle GameObject 동적 생성, 15m 반경 내 유닛 경로 무효화
────────────────────────────────────────
파일: NavMeshObstacleCleanupSystem.cs
그룹: SimulationSystemGroup (UpdateAfter: ServerDeathSystem)
역할: 건물 파괴 시 NavMeshObstacle GameObject 제거
────────────────────────────────────────
파일: PathfindingSystem.cs
그룹: SimulationSystemGroup (UpdateAfter: NavMeshObstacleSpawnSystem)
역할: IsPathDirty=true인 유닛 감지 → NavMesh.CalculatePath() → PathWaypoint 버퍼 채우기 → MovementWaypoints.Current/Next 초기화. 프레임당 최대 15개 제한
────────────────────────────────────────
파일: PathFollowSystem.cs
그룹: SimulationSystemGroup (UpdateAfter: PathfindingSystem)
역할: PathWaypoint 버퍼에서 다음 웨이포인트 꺼내서 MovementWaypoints.Next 갱신
────────────────────────────────────────
파일: PredictedMovementSystem.cs
그룹: SimulationSystemGroup (UpdateAfter: PathfindingSystem)
역할: 핵심 이동 시스템. Kinematic 방식으로 LocalTransform.Position 직접 수정. 가속/감속 적용, SpatialMaps.MovementMap을 사용한 Entity 기반 Separation 충돌 회피, 벽 충돌 미끄러짐 처리
────────────────────────────────────────
파일: MovementArrivalSystem.cs
그룹: SimulationSystemGroup (UpdateAfter: PredictedMovementSystem)
역할: 도착 판정 (거리 + !HasNext + 속도<0.05) → MovementWaypoints 비활성화, 속도 0

## Authoring (Authoring/Movement/)

| 파일 | 베이킹 컴포넌트 |
| --- | --- |
| MovementAuthoring.cs | MovementDynamics, MovementGoal, MovementWaypoints(비활성화), PathWaypoint 버퍼, NavMeshAgentConfig, Kinematic Mass |
| UnitMovementAuthoring.cs | UnitIntentState, UnitActionState, UnitCommand 버퍼. RequireComponent(MovementAuthoring) |

## 유틸리티 (Shared/Utilities/)

| 파일 | 역할 |
| --- | --- |
| MovementMath.cs | 감속 거리 계산(CalculateSlowingDistance), 목표 속도 계산(CalculateTargetSpeed), 가속/감속 적용(CalculateNewSpeed) |
| SpatialHashUtility.cs | 공간 분할 해시 계산, 셀 크기 설정, AABB 계산 |

## 컴포넌트 (Shared/Components/Movement/)

| 파일 | 역할 |
| --- | --- |
| MovementGoal.cs | 최종 목적지(Destination), 경로 재계산 플래그(IsPathDirty), 웨이포인트 인덱스 관리 |
|  MovementWaypoints.cs | 현재 이동 목표(Current), 다음 지점(Next, 코너링용), IEnableableComponent로 이동 중/정지 상태 토글 |
| MovementDynamics.cs | 유닛 이동 파라미터: MaxSpeed, Acceleration, Deceleration, RotationSpeed |
| NavMeshAgentConfig.cs | Unity NavMesh Agent Type 인덱스 참조 (유닛 크기별 경로 계산) |

## 버퍼 (Shared/Buffers/)

| 파일 | 역할 |
| --- | --- |
| PathWaypoint.cs | NavMesh 경로 계산 결과 저장. 서버 전용 (클라이언트 동기화 안함 - 대역폭 최적화) |

## 싱글톤 (Shared/Singletons/)

| 파일 | 역할 |
| --- | --- |
| SpatialMaps.cs | 공간 분할 맵 싱글톤. TargetingMap(10.0f) + MovementMap(3.0f) 저장 |