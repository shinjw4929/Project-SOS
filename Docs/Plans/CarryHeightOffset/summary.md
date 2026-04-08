# CarryHeightOffset 계획 요약

## 문제 정의
- `CarriedResourceFollowSystem`의 자원 운반 높이가 `Y+1.2f`로 하드코딩
- Worker(높이 1.4)와 Hero(높이 2.0) 등 키가 다른 유닛에 동일 오프셋 적용 → 시각적 부자연스러움

## Phase 구성
- **Phase 1**: `CarryHeightOffset` per-entity 컴포넌트 추가 + UnitAuthoring 베이킹 + FollowSystem 수정 + 문서 업데이트

## 예상 영향 범위
- 신규: `CarryHeightOffset.cs` (1개 파일)
- 수정: `UnitAuthoring.cs`, `CarriedResourceFollowSystem.cs`, `자원 채집 시스템.md` (3개 파일)
- 프리팹 설정: Worker, Hero (Unity Editor 수동)

## 자동 리뷰
- 1회차에 승인. namespace 기술 오류 1건 + 문서 업데이트 누락 1건 반영 완료.
