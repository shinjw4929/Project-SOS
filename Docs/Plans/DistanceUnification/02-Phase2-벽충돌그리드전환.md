# Phase 2: 벽 충돌 그리드 전환 (PredictedMovementSystem)

**변경 파일**: 1개 (`PredictedMovementSystem.cs`)

> **핵심**: Physics CastRay/PointDistance 벽 충돌을 그리드 셀 기반 충돌로 교체. 건물이 모두 축 정렬 직사각형이므로 정밀도 손실 없음.

---

## 현재 동작 (Physics 기반)

`PredictedMovementSystem.cs`에서 3단계로 벽 충돌 처리:

```
1. ResolveWallCollision - CastRay:
   이동 방향으로 Ray 발사 → 벽 감지 시 속도에서 법선 성분 제거 (미끄러짐)

2. ResolveWallCollision - PointDistance:
   현재 위치 주변 벽 감지 → 겹침 시 법선 방향 밀어내기

3. ClampToWall:
   이동 후 벽 관통 보정 (PointDistance × 3회 반복, 안전망)

모두 CollisionWorld + wallFilter (Layer 6, 7) 사용
```

## 신규 동작 (그리드 기반)

```
1. 이동 후 위치 계산
2. 유닛 경계(position ± ObstacleRadius)가 path-blocked 셀과 겹치는지 체크
3. 겹침 시 최소 침투 축(X 또는 Z) 방향으로 밀어내기
4. 속도에서 벽 법선 성분 제거 (항상 ±X 또는 ±Z)
```

---

## 그리드 셀 충돌 알고리즘

### 충돌 판정

```csharp
// 유닛 AABB (XZ 평면)
float minX = position.x - obstacleRadius;
float maxX = position.x + obstacleRadius;
float minZ = position.z - obstacleRadius;
float maxZ = position.z + obstacleRadius;

// AABB가 겹치는 그리드 셀 범위
int cellMinX = GridUtility.WorldToGridX(minX, gridSettings);
int cellMaxX = GridUtility.WorldToGridX(maxX, gridSettings);
int cellMinZ = GridUtility.WorldToGridZ(minZ, gridSettings);
int cellMaxZ = GridUtility.WorldToGridZ(maxZ, gridSettings);

// 겹치는 셀 중 path-blocked 셀이 있는지 검사
for (int cx = cellMinX; cx <= cellMaxX; cx++)
    for (int cz = cellMinZ; cz <= cellMaxZ; cz++)
        if (IsPathBlocked(cx, cz))
            // 충돌 처리
```

### 밀어내기 (최소 침투 축)

```csharp
// blocked 셀의 월드 경계
float cellWorldMinX = cx * CellSize + gridOrigin.x;
float cellWorldMaxX = cellWorldMinX + CellSize;
float cellWorldMinZ = cz * CellSize + gridOrigin.y;
float cellWorldMaxZ = cellWorldMinZ + CellSize;

// 겹침 계산
float overlapLeft  = maxX - cellWorldMinX;  // 유닛 오른쪽 - 셀 왼쪽
float overlapRight = cellWorldMaxX - minX;  // 셀 오른쪽 - 유닛 왼쪽
float overlapDown  = maxZ - cellWorldMinZ;
float overlapUp    = cellWorldMaxZ - minZ;

// 최소 침투 방향으로 밀어내기
float minOverlapX = math.min(overlapLeft, overlapRight);
float minOverlapZ = math.min(overlapDown, overlapUp);

if (minOverlapX < minOverlapZ)
{
    float pushDir = overlapLeft < overlapRight ? -1f : 1f;
    position.x += pushDir * minOverlapX;
}
else
{
    float pushDir = overlapDown < overlapUp ? -1f : 1f;
    position.z += pushDir * minOverlapZ;
}
```

### 속도 미끄러짐

```csharp
// 벽 법선은 항상 축 정렬 (±X 또는 ±Z)
// 밀어내기 축의 속도 성분만 제거
if (pushedOnX)
{
    float originalSpeed = math.length(velocity);  // 유닛: 속력 보존
    velocity.x = 0;
    // 유닛: 속력 복원 (적: 벡터 삭제이므로 복원 안 함)
    if (!isEnemy)
    {
        float newSpeed = math.length(velocity);
        if (newSpeed > 0.001f)
            velocity = (velocity / newSpeed) * originalSpeed;
    }
}
```

---

## path-blocked 셀 판정

PredictedMovementSystem에서 충돌 대상은 `GridCell.IsPathBlocked` 셀.

이 셀은 `GridObstacleResponseSystem`과 `ObstacleGridInitSystem`에서 `max(1, Width-2)` 규칙으로 마킹됨 (Phase 1에서 변경).

---

## 제거 대상

- `state.RequireForUpdate<PhysicsWorldSingleton>()` (OnCreate, line 27) — Physics 의존성 완전 해제
- `CollisionWorld` 접근 (SystemAPI.GetSingleton 또는 BuildPhysicsWorld 의존)
- `wallFilter` (CollisionFilter, Layer 6/7)
- `ResolveWallCollision` 메서드 전체 (Physics CastRay/PointDistance)
- `ClampToWall` 메서드 전체 (Physics PointDistance)
- `using Unity.Physics;` (미사용 using 정리)

## 추가 필요

- `GridSettings` 싱글톤 접근
- `GridCell` DynamicBuffer → `AsNativeArray()` 로 `[ReadOnly] NativeArray<GridCell>` 변환, Job 필드로 전달
- 그리드 좌표 변환 유틸리티 (`GridUtility.WorldToGridX/Z`)

```csharp
// OnUpdate에서 준비:
var gridSettings = SystemAPI.GetSingleton<GridSettings>();
var gridEntity = SystemAPI.GetSingletonEntity<GridSettings>();
var gridBuffer = SystemAPI.GetBuffer<GridCell>(gridEntity);
var gridArray = gridBuffer.AsNativeArray();  // ReadOnly NativeArray view

// Job에 전달:
var job = new KinematicMovementJob
{
    GridCells = gridArray,       // [ReadOnly] NativeArray<GridCell>
    GridSettings = gridSettings, // GridSettings struct (값 복사)
    // ...
};
```

---

## 유의사항

- **Flying 유닛**: `iAmFlying` 체크는 유지. Flying 유닛은 벽 충돌 무시 (기존 동작 그대로).
- **유닛 vs 적 속도 처리**: 유닛은 속력(magnitude) 보존, 적은 벡터 삭제. 이 분기 유지.
- **안전망 반복**: ClampToWall의 3회 반복 패턴을 그리드 버전에서도 유지 (코너에서 두 벽 교차).
- **Separation 진동**: 기존 PredictedMovementSystem의 separation 진동 감지 로직은 벽 충돌과 무관, 변경 불필요.

---

## 체크리스트

- [ ] `PredictedMovementSystem.cs`: `ResolveWallCollision` → 그리드 셀 충돌 재구현
- [ ] `PredictedMovementSystem.cs`: `ClampToWall` → 그리드 셀 충돌 재구현 (3회 반복 유지)
- [ ] `PredictedMovementSystem.cs`: `CollisionWorld`, `wallFilter` 관련 코드 제거
- [ ] `PredictedMovementSystem.cs`: `GridSettings` + `GridCell` 버퍼 접근 추가
- [ ] 유닛/적 속도 보존 분기 유지
- [ ] Flying 유닛 벽 충돌 무시 유지
- [ ] PlayMode 테스트: 벽 미끄러짐, 코너 관통 방지, 건물 건설 후 밀어내기 확인
