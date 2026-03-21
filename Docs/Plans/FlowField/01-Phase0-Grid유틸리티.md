# Phase 0: Grid 유틸리티 확장

**파일**: `Assets/Scripts/Shared/Utilities/GridUtility.cs`

---

## Burst 어노테이션 규칙

모든 신규 메서드는 struct 파라미터(`int2`, `GridSettings`, `NativeArray<byte>` 등)를 받으므로 개별 `[BurstCompile]` 적용 불가 (BC1064). 기존 `GridUtility` 패턴에 따라 `[MethodImpl(MethodImplOptions.AggressiveInlining)]`만 적용한다.

---

## 추가 메서드

### CellCenterToWorld
```csharp
// 단일 셀 중심의 월드 좌표 반환 (XZ 평면, Y=0)
// FlowFieldSteeringSystem에서 다음 셀 목표 좌표 계산에 사용
// 사용처: FlowFieldSteeringSystem (Phase 2.5)
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static float3 CellCenterToWorld(int2 cell, GridSettings settings)
{
    // GridOrigin은 float2 → float3 변환 필요 (기존 GridToWorld 패턴)
    float2 center = settings.GridOrigin + (new float2(cell) + 0.5f) * settings.CellSize;
    return new float3(center.x, 0, center.y);
}
```

### IsPassable
```csharp
// 범위 체크 + 점유 체크
// 경계 밖은 blocked(통과 불가)로 처리
// 사용처: IsPassableForSize 내부 헬퍼, FlowFieldCore.ComputeField BFS 내부에서는 passabilityMap 직접 인덱싱
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static bool IsPassable(NativeArray<byte> map, int x, int y, int gridSizeX, int gridSizeY)
```

### IsPassableForSize
```csharp
// 유닛 크기 고려 — 주변 셀 확장 체크
// cellPadding=0 → Small (자기 셀만), cellPadding=1 → Large (주변 1칸 포함)
// cellPadding=0일 때 IsPassable과 동일한 동작
// 경계 밖으로 확장되는 경우 blocked로 처리 (대형 유닛의 맵 가장자리 진입 차단)
// 사용처: BuildPassabilityMap 내부
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static bool IsPassableForSize(NativeArray<byte> map, int x, int y, int gridSizeX, int gridSizeY, int cellPadding)
```

### BuildPassabilityMap
```csharp
// GridCell 버퍼 → byte 맵 변환
// 0 = passable, 1 = blocked
// 전제 조건: output는 호출자가 gridSize.x * gridSize.y 크기로 할당해야 함
// 사용처: FlowFieldSystem Collect Phase (Phase 2)
// [MethodImpl] 미적용: 루프 포함 메서드이므로 인라이닝 제외 (기존 GridUtility 패턴 동일)
public static void BuildPassabilityMap(DynamicBuffer<GridCell> cells, int2 gridSize, int cellPadding, NativeArray<byte> output)
```

---

## 체크리스트

- [ ] `CellCenterToWorld` 구현 (float2→float3 변환, Y=0)
- [ ] `IsPassable` 구현 (경계 밖 = blocked)
- [ ] `IsPassableForSize` 구현 (cellPadding 파라미터, 경계 밖 = blocked)
- [ ] `BuildPassabilityMap` 구현
- [ ] 루프 없는 메서드에 `[MethodImpl(MethodImplOptions.AggressiveInlining)]` 적용 (BuildPassabilityMap 제외)
- [ ] 모든 메서드에 `public` 접근 제한자 (다른 어셈블리 Server에서 호출)
- [ ] 기존 GridUtility 메서드와 네이밍/파라미터 일관성 확인
