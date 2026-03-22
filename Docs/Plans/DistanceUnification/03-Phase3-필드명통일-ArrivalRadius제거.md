# Phase 3: Authoring 필드명 통일 + ArrivalRadius 제거

**변경 파일**: 3개 (UnitAuthoring, EnemyAuthoring, MovementAuthoring)

> **핵심**: ObstacleRadius 컴포넌트에 매핑되는 Authoring 필드명을 `obstacleRadius`로 통일. ArrivalRadius Authoring 필드 제거 (fallback 활용).

---

## 3-A. ObstacleRadius 필드명 통일

### UnitAuthoring

**파일**: `Assets/Scripts/Authoring/Entities/UnitAuthoring.cs`

```csharp
// Before:
[Header("Unit Size")]
public float radius = 1.0f;

// After:
[Header("Obstacle Radius")]
[FormerlySerializedAs("radius")]
public float obstacleRadius = 1.0f;
```

Baker 변경: `authoring.radius` → `authoring.obstacleRadius`

### EnemyAuthoring

**파일**: `Assets/Scripts/Authoring/Entities/EnemyAuthoring.cs`

```csharp
// Before:
[Header("Collision")]
[Min(0.1f)] public float radius = 1.5f;

// After:
[Header("Obstacle Radius")]
[FormerlySerializedAs("radius")]
[Min(0.1f)] public float obstacleRadius = 1.5f;
```

Baker 변경: `authoring.radius` → `authoring.obstacleRadius`

> **Note**: StructureAuthoring/ResourceNodeAuthoring의 `interactionRadius` → `obstacleRadius` 리네이밍은 Phase 1에서 이미 처리.

---

## 3-B. ArrivalRadius 제거

### MovementAuthoring

**파일**: `Assets/Scripts/Authoring/Movement/MovementAuthoring.cs`

```csharp
// Before:
[Header("Pathfinding")]
[Tooltip("도착 판정 반경")]
public float ArrivalRadius = 0.5f;

// After: ArrivalRadius 필드 제거
```

Baker 변경:
```csharp
// Before:
AddComponent(entity, new MovementWaypoints
{
    Current = float3.zero,
    Next = float3.zero,
    HasNext = false,
    ArrivalRadius = authoring.ArrivalRadius  // ← 제거
});

// After:
AddComponent(entity, new MovementWaypoints
{
    Current = float3.zero,
    Next = float3.zero,
    HasNext = false,
    ArrivalRadius = 0  // fallback 작동: ObstacleRadius + 0.1f
});
```

### 안전성 확인

`MovementArrivalSystem.cs` (line 87-89)에 이미 fallback 존재:
```csharp
float arrivalRadius = destination.ArrivalRadius > 0
    ? destination.ArrivalRadius
    : obstacle.Radius + 0.1f;
```

`ArrivalRadius = 0`이면 항상 `ObstacleRadius + 0.1f`가 사용됨.

`BuildArrivalSystem`은 런타임에 `ArrivalUtility.GetSafeArrivalRadius()`로 동적 설정하므로 영향 없음.

`MovementWaypoints.ArrivalRadius` **컴포넌트 필드는 유지** — BuildArrivalSystem이 런타임에 값을 쓰므로.

---

## 체크리스트

- [ ] `UnitAuthoring.cs`: `radius` → `obstacleRadius` + `[FormerlySerializedAs("radius")]` + `[Header("Obstacle Radius")]`
- [ ] `EnemyAuthoring.cs`: `radius` → `obstacleRadius` + `[FormerlySerializedAs("radius")]` + `[Header("Obstacle Radius")]`
- [ ] `MovementAuthoring.cs`: `ArrivalRadius` 필드 제거, Baker에서 `ArrivalRadius = 0` 고정
- [ ] 프리팹 Inspector에서 기존 값 유지 확인 (`[FormerlySerializedAs]` 검증)
