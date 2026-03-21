# Phase 5: 테스트

---

## 어셈블리 참조

`FlowFieldCore`는 `Shared/Utilities/`에 위치 (Phase 1에서 결정). `EditModeTests.asmdef`이 `Shared`를 참조하므로 EditMode 테스트에서 직접 접근 가능.

---

## EditMode 테스트

> 파일 위치: `Assets/Tests/EditMode/Utilities/` (기존 `GridUtilityTests.cs` 패턴)
> NativeArray 사용 시 `[TearDown]`에서 반드시 Dispose

### FlowFieldCore.ComputeField
- [ ] 빈 그리드 → 모든 셀이 목적지를 향하는 방향
- [ ] 장애물 우회 → 벽 뒤 셀도 우회 방향 정상
- [ ] 대각 이동 → 코너 차단 동작 (인접 직교 셀 blocked 시 대각 불가)
- [ ] 도달 불가 셀 → 방향 255(None)

### GridUtility.CellCenterToWorld
- [ ] 셀 (0,0) → GridOrigin + 0.5*CellSize (float3, Y=0)
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
- [ ] 특정 셀 점유 → 해당 셀만 1
- [ ] cellPadding=1 → 점유 셀 주변도 blocked

---

## PlayMode 테스트

> 파일 위치: `Assets/Tests/PlayMode/Systems/`
> 선행 작업: `ECSTestBase` 확장 (Grid 싱글톤 헬퍼, 이동 유닛 생성 헬퍼, FlowFieldCacheData 생성 헬퍼)

### 기본 이동
- [ ] 유닛 이동 명령 → Flow Field 생성 → 목적지 도착

### 필드 공유
- [ ] 다수 유닛(10+) 동일 목적지 → 필드 1개만 생성 확인

### 동적 장애물
- [ ] 건물 건설 → 캐시 무효화 → 우회 경로로 재경로
- [ ] 건물 파괴 → 캐시 무효화 → 직선 경로 복구
- [ ] **IsGridStale 프레임 스킵**: 건설 직후 프레임에서 BFS 재계산 스킵 → 다음 프레임에 정상 재계산

### 핵심 버그 검증
- [ ] **Tank/EnemyBig 벽 사이 통과 불가** (Large passability 맵 검증)
- [ ] **Small/Large 혼합**: 좁은 통로(폭 2셀)에서 Small 유닛은 통과, Large 유닛은 우회
- [ ] Flying 유닛 벽 통과 확인 (Flow Field 스킵, 직선 이동)

### Partial Path
- [ ] 완전히 둘러싸인 목적지 → 가장 가까운 도달 가능 셀까지 이동

### 캐시 무효화
- [ ] 무효화 후 모든 필드 조회 실패 (HashMap empty 확인)
- [ ] 무효화 후 재계산 정상

### 성능
- [ ] 대량 유닛(50+) 동시 이동 — 프레임 드랍 없음
- [ ] BFS 계산 시간: 100x100 그리드에서 단일 필드 < 0.5ms
- [ ] 50유닛 동시 이동: FlowFieldSystem + FlowFieldSteeringSystem 합산 < 2ms
- [ ] 캐시 히트 시 BFS 재계산 없음 확인

---

## 체크리스트 요약

- [ ] `ECSTestBase` 확장 (Grid 싱글톤, 이동 유닛, FlowFieldCacheData 헬퍼)
- [ ] EditMode 테스트 5종 통과 (FlowFieldCore, CellCenterToWorld, IsPassable, IsPassableForSize, BuildPassabilityMap)
- [ ] PlayMode 테스트 8종 통과 (기본 이동, 필드 공유, 동적 장애물, 핵심 버그, Partial Path, 캐시 무효화, 성능)
- [ ] 성능 프로파일링 (BFS 계산 시간, 캐시 히트율)
