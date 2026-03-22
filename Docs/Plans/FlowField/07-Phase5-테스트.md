# Phase 5: 테스트

---

## 어셈블리 참조

`FlowFieldCore`는 `Shared/Utilities/`에 위치 (Phase 1에서 결정). `EditModeTests.asmdef`이 `Shared`를 참조하므로 EditMode 테스트에서 직접 접근 가능.

---

## EditMode 테스트

> 파일 위치: `Assets/Tests/EditMode/Utilities/` (기존 `GridUtilityTests.cs` 패턴)
> NativeArray 사용 시 `[TearDown]`에서 반드시 Dispose
> **그리드 기본 설정**: CellSize=0.5f, GridSize=200×200 (0.5m 셀 기준)

### FlowFieldCore.ComputeField
- [ ] 빈 그리드 → 모든 셀이 목적지를 향하는 방향
- [ ] 장애물 우회 → 벽 뒤 셀도 우회 방향 정상
- [ ] 대각 이동 → 코너 차단 동작 (인접 직교 셀 blocked 시 대각 불가)
- [ ] 도달 불가 셀 → 방향 255(None)

### GridUtility.CellCenterToWorld
- [ ] 셀 (0,0) → GridOrigin + 0.25 (0.5*CellSize=0.5*0.5=0.25, float3, Y=0)
- [ ] 임의 셀 round-trip: WorldToGrid(CellCenterToWorld(cell)) == cell

### GridUtility.IsPassable
- [ ] passable 셀 → true
- [ ] blocked 셀 → false
- [ ] 경계 밖 좌표 → false

### GridUtility.IsPassableForSize
- [ ] cellPadding=0 (Small) → 자기 셀만 체크
- [ ] cellPadding=1 (Large) → 주변 1칸 포함 체크
- [ ] 경계 셀 처리 (맵 가장자리, 확장 시 경계 밖 = blocked)

### GridUtility.BuildPassabilityMap
- [ ] 모든 셀 비점유 → 모두 0
- [ ] 특정 셀 IsPathBlocked=true → 해당 셀만 1
- [ ] cellPadding=1 → IsPathBlocked 셀 주변도 blocked
- [ ] **IsOccupied=true, IsPathBlocked=false인 셀 → passability 맵에서 0 (통과 가능)** — 핵심: 배치 점유와 경로탐색 차단이 분리됨을 확인

### GridUtility.MarkPathBlocked / UnmarkPathBlocked
- [ ] Wall(4×4, path 2×2): 배치 좌표 (0,0) → IsPathBlocked 셀 (1,1)-(2,2)만 마킹
- [ ] Barracks(6×6, path 6×6): 배치 좌표 (0,0) → IsPathBlocked 셀 (0,0)-(5,5) 전체 마킹
- [ ] UnmarkPathBlocked 후 IsPathBlocked 복원 확인
- [ ] **멱등성**: 같은 셀 2번 MarkPathBlocked → 값 변화 없음 (이미 1)
- [ ] **인접 건물 중첩 마킹**: 2개 건물의 경로탐색 풋프린트가 겹치지 않는 경우, 각각 마킹 후 정확한 영역만 blocked 확인

---

## PlayMode 테스트

> 파일 위치: `Assets/Tests/PlayMode/Systems/`
> 선행 작업: `ECSTestBase` 확장 (Grid 싱글톤 0.5m 헬퍼, 이동 유닛 생성 헬퍼, FlowFieldCacheData 생성 헬퍼)

### 기본 이동
- [ ] 유닛 이동 명령 → Flow Field 생성 → 목적지 도착

### 필드 공유
- [ ] 다수 유닛(10+) 동일 목적지 → 필드 1개만 생성 확인

### 동적 장애물
- [ ] 건물 건설 → 캐시 무효화 → 우회 경로로 재경로
- [ ] 건물 파괴 → 캐시 무효화 → 직선 경로 복구
- [ ] **즉시 재계산**: 건설 직후 같은 프레임에서 passability 맵 재빌드 + BFS 재계산이 수행됨을 검증

### 벽 반투과 (핵심 버그 검증)

테스트 맵: Grid 200x200 (0.5m 셀), 원점 (0,0)
벽 구조: 배치 4x4 셀(2mx2m), 경로탐색 2x2 셀(1mx1m) 중앙

```
TC1 (빈틈없이 배치):
  Wall-A: 배치 (20,50)~(23,53), 경로탐색 (21,51)~(22,52)
  Wall-B: 배치 (24,50)~(27,53), 경로탐색 (25,51)~(26,52)
  gap = (23,51)~(24,52) — 2셀

  Small맵 (패딩 0): gap 통과 가능
  Large맵 (패딩 1): gap 주변 확장 → 전부 차단
```

- [ ] **TC1-Small 통과**: 위 배치에서 Small 유닛 (15,51) → (32,51) 이동 성공
- [ ] **TC1-Large 차단**: 위 배치에서 Large 유닛 동일 경로 차단 (우회 필요)
- [ ] **TC2-1칸 띄움 Large 통과**: Wall-A (20,50), Wall-B (28,50) — gap 4셀 → Large 유닛 통과
- [ ] **Small/Large 혼합**: 같은 벽 라인에서 Small은 통과, Large는 우회하는지 동시 검증
- [ ] Flying 유닛 벽 통과 확인 (Flow Field 스킵, 직선 이동)

### Partial Path
- [ ] 완전히 둘러싸인 목적지 → 가장 가까운 도달 가능 셀까지 이동

### 캐시 무효화
- [ ] 무효화 후 모든 필드 조회 실패 (HashMap empty 확인)
- [ ] 무효화 후 재계산 정상

### 캐시 히트/LRU
- [ ] **캐시 히트율**: 10개 유닛 동일 목적지 → BFS 1회만 실행 (캐시 히트 9회)
- [ ] **LRU 교체**: 33개 고유 목적지 요청(maxFields=32) → 가장 오래된 필드가 교체됨

### 성능
- [ ] 대량 유닛(50+) 동시 이동 — FlowFieldSystem + FlowFieldSteeringSystem 합산 < 2ms
- [ ] BFS 계산 시간: 200x200 그리드에서 단일 필드 < 0.5ms (측정: Stopwatch 1000회 반복 평균)
- [ ] 50유닛 동시 이동, 5개 다른 목적지: 캐시 미스 5회 + 히트 45회
- [ ] 캐시 히트 시 BFS 재계산 없음 확인

---

## 체크리스트 요약

- [ ] `ECSTestBase` 확장 (Grid 싱글톤 0.5m, 이동 유닛, FlowFieldCacheData 헬퍼)
- [ ] EditMode 테스트 7종 통과 (FlowFieldCore, CellCenterToWorld, IsPassable, IsPassableForSize, BuildPassabilityMap, MarkPathBlocked/UnmarkPathBlocked + 멱등성)
- [ ] PlayMode 테스트 10종 통과 (기본 이동, 필드 공유, 동적 장애물, 벽 반투과, Partial Path, 캐시 무효화, 캐시 히트율, LRU 교체, 성능)
- [ ] 성능 프로파일링: BFS < 0.5ms (200x200), 50유닛 시스템 합산 < 2ms
