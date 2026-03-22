# Phase 6: 매직 넘버 문서화 + Collider 규칙 정립

**변경 파일**: Utility/System 파일 (주석만) + Architecture.md + CLAUDE.md

> **핵심**: 코드 변경 없음. 산재된 상수에 단위/의미 주석 보강, Collider 역할 재정의 문서화.

---

## 6-A. 매직 넘버 주석 보강

각 상수에 `/// <summary>` XML 주석 추가. 단위(월드 단위 m, m/s, 초 등)와 용도 명시.

### ArrivalUtility.cs

```csharp
/// <summary>타겟의 ObstacleRadius 정보가 없을 때 사용하는 기본 반지름 (월드 단위, m)</summary>
const float DefaultTargetRadius = 1.5f;

/// <summary>접근점 계산 시 타겟 표면에서의 여유 거리 (월드 단위, m)</summary>
const float ApproachMargin = 0.1f;
```

### MovementMath.cs

```csharp
/// <summary>도착 판정 임계 거리 (월드 단위, m). 이 거리 이내이면 도착으로 판정.</summary>
const float ArrivalThreshold = 0.3f;

/// <summary>웨이포인트 전환 거리 (월드 단위, m). 다음 웨이포인트로 코너링 시작.</summary>
const float CornerRadius = 0.5f;

/// <summary>정지 보정 거리 (월드 단위, m). 진동 방지용 스냅.</summary>
const float SnapDistance = 0.02f;

/// <summary>최소 이동 속도 (m/s). 이 이하이면 정지 처리.</summary>
const float MinSpeed = 0.5f;
```

### SpatialHashUtility.cs

```csharp
/// <summary>타겟팅용 공간분할 셀 크기 (월드 단위, m). VisionRange/AggroRange 탐색에 사용.</summary>
const float TargetingCellSize = 10.0f;

/// <summary>이동/충돌 회피용 공간분할 셀 크기 (월드 단위, m). Separation 계산에 사용.</summary>
const float MovementCellSize = 3.0f;
```

### CombatUtility.cs

```csharp
/// <summary>투사체 발사 시작점의 Y 오프셋 (월드 단위, m). 지면이 아닌 유닛 중심 높이에서 발사.</summary>
const float ProjectileHeightOffset = 1f;
```

### GridObstacleResponseSystem.cs

```csharp
/// <summary>건물 건설 시 주변 유닛 경로 무효화 반지름 (월드 단위, m).</summary>
const float PathInvalidationRadius = 8f;
```

### GridObstacleCleanupSystem.cs

```csharp
/// <summary>건물 파괴 시 Partial Path 유닛 경로 무효화 반지름 (월드 단위, m).</summary>
const float PartialPathInvalidationRadius = 12f;
```

### WanderUtility.cs

```csharp
/// <summary>정체 판정 이동 거리 (월드 단위, m). StuckCheckInterval 동안 이 거리 미만 이동 시 정체.</summary>
const float StuckThreshold = 2.0f;
```

---

## 6-B. Collider 역할 규칙 문서화

### Architecture.md 추가 내용

```markdown
## Collider 역할 규칙

### 용도 제한
- Collider는 **raycast (선택, 건설 검증) + 투사체 충돌** 전용
- 물리 충돌 (벽 미끄러짐, 건설 시 push-out)은 **그리드 기반** — Collider 사용 금지

### 크기 정합성
- **유닛/적**: Capsule/Sphere Collider 반지름 ≈ ObstacleRadius (raycast 히트 영역)
- **건물/자원**: Box Collider 크기 ≈ Width × Length × CellSize (raycast 히트 영역)
- Collider는 자동 베이킹 (코드 생성 금지)
- 크기 정합성은 프리팹 설정 시 수동 확인

### 물리 충돌 = 그리드 단일 소스
- 건물 크기: StructureFootprint.Width/Length (그리드 셀 단위)
- 경로 차단: max(1, Width-2) × max(1, Length-2) (중앙 영역)
- Push-out: Width × CellSize / 2 (직사각형, 항상)
- 벽 미끄러짐: GridCell.IsPathBlocked 셀 경계 (PredictedMovementSystem)
```

### CLAUDE.md 추가 내용

```markdown
### Collider Rules
1. **Collider 용도**: raycast(선택, 건설 검증) + 투사체 충돌 전용. 물리 충돌에 Collider 사용 금지.
2. **물리 충돌**: 그리드 셀(GridCell.IsPathBlocked) 기반. PredictedMovementSystem, GridObstacleResponseSystem 참조.
3. **Collider 크기**: 유닛/적 Capsule 반지름 ≈ ObstacleRadius, 건물 Box ≈ Width × Length × CellSize.
```

---

## 6-C. StructureFootprint 필드 용도 매핑

### Architecture.md 추가 내용

```markdown
## StructureFootprint 필드 매핑 (리팩토링 후)

| 필드 | 단위 | 참조 시스템 | 용도 |
|------|------|-----------|------|
| Width | 그리드 셀 (0.5m) | GridOccupancyEventSystem, ObstacleGridInitSystem, HandleBuildRequestSystem, BuildArrivalSystem, GridObstacleResponseSystem, GridObstacleCleanupSystem, InitialWallDecaySystem, ProductionProgressSystem | 배치 점유, 경로 차단 (W-2 파생), push-out (W×CellSize 파생) |
| Length | 그리드 셀 (0.5m) | 동일 | 동일 |
| Height | 월드 단위 (m) | BuildArrivalSystem | 건물 높이 (위치 계산) |

### 파생값
- PathWidth = max(1, Width - 2): 경로탐색 차단 중앙 영역
- PathLength = max(1, Length - 2)
- PushHalfW = Width × CellSize / 2: 건설 시 유닛 밀어내기 반폭
- PushHalfL = Length × CellSize / 2
```

---

## 체크리스트

- [ ] `ArrivalUtility.cs`: `DefaultTargetRadius`, `ApproachMargin` 주석 추가
- [ ] `MovementMath.cs`: `ArrivalThreshold`, `CornerRadius`, `SnapDistance`, `MinSpeed` 주석 추가
- [ ] `SpatialHashUtility.cs`: `TargetingCellSize`, `MovementCellSize` 주석 추가
- [ ] `CombatUtility.cs`: `ProjectileHeightOffset` 주석 추가
- [ ] `GridObstacleResponseSystem.cs`: `PathInvalidationRadius` 주석 추가
- [ ] `GridObstacleCleanupSystem.cs`: `PartialPathInvalidationRadius` 주석 추가
- [ ] `WanderUtility.cs`: `StuckThreshold` 주석 추가
- [ ] `Docs/Architecture.md`: Collider 규칙 + StructureFootprint 매핑 추가
- [ ] `CLAUDE.md`: Collider Rules 섹션 추가
