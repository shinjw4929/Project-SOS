# 건설 도착 판정 AABB 표면 거리 전환 + Separation 면제 확대

**날짜**: 2026-03-25
**작업**: 건설 도착 판정을 중심 원형 거리에서 AABB 표면 거리² 기반으로 전환, 건설/채집 유닛 Separation 면제 확대

---

## 문제

1. **건설 giveup**: 대각선/모서리 방향에서 접근 시 건물 바로 옆인데도 중심까지 거리가 멀어서 도착 판정 실패
2. **Separation 미면제**: 건설 중(Intent.Build) 유닛이 다른 유닛에 의해 밀려 건설 실패

## 핵심 변경

### 1. AABB 표면 거리 기반 도착 판정

```
변경 전: dist(빌더, 중심) <= StructureRadius + workRange + cellSize  (원형)
변경 후: distSqToAABB(빌더, footprint) <= (workRange + cellSize)²   (사각형 표면)
```

- 방향 독립적 판정 — 정면/대각선/모서리 어디서든 건물 표면까지 거리로 판정
- sqrt 회피 — 제곱 거리 비교로 성능 최적화

### 2. BuildApproachRadius AABB 조기 정지

FlowFieldSteeringSystem에서 건설 이동 유닛의 조기 정지도 AABB 표면 거리 기반으로 통일.

```
BuildApproachRadius {
    Value = workRange,          // 표면 기준 정지 거리
    Center = BuildSiteCenter,   // 건물 중심
    HalfW/HalfL                 // Footprint 절반 (월드 단위)
}
```

### 3. Separation 면제 확대

PredictedMovementSystem에서 `iAmGathering` → `iAmWorking`으로 확대:
- 기존: Gather 유닛끼리만 면제
- 변경: Gather 또는 Build 유닛은 한쪽이라도 작업 중이면 면제 (적 제외)

---

## 변경 파일

| 파일 | 변경 유형 |
|------|----------|
| `Shared/Utilities/ArrivalUtility.cs` | `DistanceSqToAABBSurfaceXZ` 메서드 추가 |
| `Server/Data/BuildApproachRadius.cs` | Center, HalfW, HalfL 필드 추가 (신규 파일) |
| `Server/Systems/Commands/Construction/HandleBuildMoveRequestSystem.cs` | Destination=BuildSiteCenter, 카탈로그 Footprint 조회, BuildApproachRadius AABB 정보 설정 |
| `Server/Systems/Movement/FlowFieldSteeringSystem.cs` | BuildApproachRadius Lookup 추가, AABB 조기 정지 로직 |
| `Server/Systems/Commands/Construction/BuildArrivalSystem.cs` | AABB 표면 거리² 기반 도착 판정, BuildApproachRadius 제거 로직 |
| `Server/Systems/Movement/PredictedMovementSystem.cs` | Separation 면제 Gather → Gather/Build 확대 |
| `Docs/Systems/건설 시스템.md` | AABB 판정, BuildApproachRadius 반영 |
| `Docs/Systems/엔티티 이동 시스템(FlowField).md` | Separation 면제 확대, FlowFieldSteeringSystem 조기 정지 반영 |

---

## 핵심 설계 결정

1. **AABB 표면 거리**: 건물이 사각형 Footprint이므로 원형 거리보다 정확. 스타크래프트 방식과 동일.
2. **제곱 거리 비교**: `DistanceSqToAABBSurfaceXZ`로 sqrt 비용 회피. 판정 로직에서만 사용하므로 실제 거리 불필요.
3. **카탈로그 Lookup**: HandleBuildMoveRequestSystem에서 StructureCatalog → StructureFootprint로 Footprint 조회. RPC 확장 없이 Server Authority 유지.
