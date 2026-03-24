# Phase 4: 테스트 + 문서

## 목표

단위 테스트 추가 + 문서 정합성 확보.

---

## 4-A. 단위 테스트

**파일**: `Assets/Tests/EditMode/Utilities/ArrivalUtilityTests.cs` (기존 파일에 추가)

기존 파일에 이미 `CalculateApproachPoint`, `GetInteractionArrivalDistance`, `GetSafeArrivalRadius`, `IsWithinInteractionRange`, `IsWithinInteractionRangeXZ` 테스트가 있다. 새 메서드 테스트를 기존 파일 끝(`#endregion` 아래, 클래스 닫기 전)에 추가.

### GetGridCompensatedArrivalDistance 테스트

```csharp
#region GetGridCompensatedArrivalDistance Tests

[Test]
public void GetGridCompensatedArrivalDistance_AddsCellSizeMargin()
{
    float result = ArrivalUtility.GetGridCompensatedArrivalDistance(6.0f, 0.7f, 1.0f);

    // 6.0 + 0.7 + 1.0 = 7.7
    Assert.AreEqual(7.7f, result, 0.001f);
}

[Test]
public void GetGridCompensatedArrivalDistance_ZeroCellSize_EqualsBasicArrival()
{
    float compensated = ArrivalUtility.GetGridCompensatedArrivalDistance(2.0f, 1.0f, 0f);
    float basic = ArrivalUtility.GetInteractionArrivalDistance(2.0f, 1.0f);

    Assert.AreEqual(basic, compensated, 0.001f);
}

/// <summary>
/// ResourceCenter 반납 시나리오: Width=8, CellSize=1.0, ObstacleRadius=6.0, WorkRange=0.7
/// 워커 정지 거리 ~7.55m에서 도착 성공해야 함
/// </summary>
[Test]
public void GetGridCompensatedArrivalDistance_ResourceCenterScenario_WorkerCanArrive()
{
    float obstacleRadius = 6.0f;
    float workRange = 0.7f;
    float cellSize = 1.0f;
    float workerStopDistance = 7.55f;

    float arrivalDist = ArrivalUtility.GetGridCompensatedArrivalDistance(
        obstacleRadius, workRange, cellSize);

    // 7.7m > 7.55m → 도착 성공
    Assert.Greater(arrivalDist, workerStopDistance);
}

/// <summary>
/// BuildArrivalSystem 시나리오: StructureRadius + WorkRange + CellSize
/// 기존 CellSize * 0.7 대비 마진 증가 확인
/// </summary>
[Test]
public void GetGridCompensatedArrivalDistance_BuildScenario_LargerThanOldMargin()
{
    float structureRadius = 4.5f; // Barracks
    float workRange = 0.7f;
    float cellSize = 1.0f;

    float newArrival = ArrivalUtility.GetGridCompensatedArrivalDistance(
        structureRadius, workRange, cellSize);
    float oldArrival = ArrivalUtility.GetInteractionArrivalDistance(
        structureRadius, workRange) + cellSize * 0.7f;

    // 6.2 > 5.9
    Assert.Greater(newArrival, oldArrival);
}

#endregion
```

### GetEffectiveRadius 테스트

```csharp
#region GetEffectiveRadius Tests

/// <summary>
/// 현재 모든 건물: ObstacleRadius > gridBlockedHalfExtent → ObstacleRadius 반환
/// </summary>
[Test]
public void GetEffectiveRadius_ObstacleRadiusLarger_ReturnsObstacleRadius()
{
    // ResourceCenter: Width=8, OR=6.0, CellSize=1.0
    // gridBlockedHalfExtent = max(1, 8-2) * 1.0 * 0.5 = 3.0
    // effectiveRadius = max(6.0, 3.0 + 0.5) = 6.0
    var footprint = new StructureFootprint { Width = 8, Length = 8, Height = 5f };

    float result = ArrivalUtility.GetEffectiveRadius(6.0f, in footprint, 1.0f);

    Assert.AreEqual(6.0f, result, 0.001f);
}

[Test]
public void GetEffectiveRadius_Wall_ReturnsObstacleRadius()
{
    // Wall: Width=4, OR=3.0, CellSize=1.0
    // gridBlockedHalfExtent = max(1, 4-2) * 1.0 * 0.5 = 1.0
    // effectiveRadius = max(3.0, 1.0 + 0.5) = 3.0
    var footprint = new StructureFootprint { Width = 4, Length = 4, Height = 3f };

    float result = ArrivalUtility.GetEffectiveRadius(3.0f, in footprint, 1.0f);

    Assert.AreEqual(3.0f, result, 0.001f);
}

/// <summary>
/// 가상 시나리오: ObstacleRadius < gridBlockedHalfExtent → gridBlockedHalfExtent + CellSize*0.5 반환
/// </summary>
[Test]
public void GetEffectiveRadius_GridRadiusLarger_ReturnsGridBased()
{
    // 가상 건물: Width=12, OR=2.0, CellSize=1.0
    // gridBlockedHalfExtent = max(1, 12-2) * 1.0 * 0.5 = 5.0
    // effectiveRadius = max(2.0, 5.0 + 0.5) = 5.5
    var footprint = new StructureFootprint { Width = 12, Length = 12, Height = 5f };

    float result = ArrivalUtility.GetEffectiveRadius(2.0f, in footprint, 1.0f);

    Assert.AreEqual(5.5f, result, 0.001f);
}

[Test]
public void GetEffectiveRadius_SmallBuilding_MinPathWidthOne()
{
    // Width=2 → pathWidth = max(1, 2-2) = max(1, 0) = 1
    // gridBlockedHalfExtent = 1 * 1.0 * 0.5 = 0.5
    // effectiveRadius = max(1.0, 0.5 + 0.5) = 1.0
    var footprint = new StructureFootprint { Width = 2, Length = 2, Height = 2f };

    float result = ArrivalUtility.GetEffectiveRadius(1.0f, in footprint, 1.0f);

    Assert.AreEqual(1.0f, result, 0.001f);
}

#endregion
```

---

## 4-B. 문서 업데이트

### `Docs/Systems/자원 채집 시스템.md`

도착 판정 관련 섹션에 CellSize 마진 반영:

- 도착 거리 공식: `ObstacleRadius + WorkRange` → `ObstacleRadius + WorkRange + CellSize`
- 거리 방식: 3D → XZ
- ArrivalRadius: TransitionToReturn, DecideNextAction에서 설정

### `Docs/Systems/건설 시스템.md`

도착 검증 관련 섹션:

- 도착 거리 공식: `GetInteractionArrivalDistance + CellSize * 0.7` → `GetGridCompensatedArrivalDistance` (CellSize * 1.0)

### `Docs/Internal/Analysis/도착 판정 전수 검사.md`

해결 상태 업데이트:

- P0 [G-1] ProcessMovingToReturn: **해결됨** (CellSize 마진 + XZ 거리)
- P1 [G-2] ProcessMovingToGather: **해결됨**
- P1 [G-3] ProcessWaitingForNode: **해결됨**
- P1 [G-4] TransitionToReturn ArrivalRadius: **해결됨**
- P1 [G-5] DecideNextAction ArrivalRadius: **해결됨**
- P2 [S-1] 3D vs XZ 혼용: **채집 시스템 해결됨** (전투 시스템은 3D 유지 — 비행 유닛)
- P2 [S-2] 접근점 blocked 영역: **effectiveRadius 안전장치 적용**

---

## 변경 파일 요약

| 파일 | 변경 내용 |
|------|----------|
| `Assets/Tests/EditMode/Utilities/ArrivalUtilityTests.cs` | `GetGridCompensatedArrivalDistance` 4개 + `GetEffectiveRadius` 4개 테스트 추가 |
| `Docs/Systems/자원 채집 시스템.md` | CellSize 마진, XZ 거리, ArrivalRadius 설정 반영 |
| `Docs/Systems/건설 시스템.md` | CellSize 마진 통일 반영 |
| `Docs/Internal/Analysis/도착 판정 전수 검사.md` | 해결 상태 업데이트 |
