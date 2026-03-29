# FormationFix 요약

## 문제 정의
포메이션 오프셋이 (1) 건물 둘레 분산 유닛에게 비정상 경로를 생성하고 (2) 벽/맵 밖에 목적지를 설정하는 문제.

## 이전 시도와의 차이

| 항목 | 이전 시도 (실패) | 이번 계획 |
|---|---|---|
| 그리드 접근 | `RequireForUpdate<GridSettings>()` | `TryGetSingletonEntity` (시스템 동작 보장) |
| 그리드 없을 때 | 시스템 미실행 (RPC 처리 중단) | 기존 동작 유지 (그리드 검증만 건너뜀) |
| 오버로드 | 단일 메서드에 분기 | 2개 오버로드 (그리드 있음/없음) |

## Phase 구성
- Phase 1: HandleMoveRequestSystem에 그리드 검증 추가 (1개 파일 변경)

## 예상 영향 범위
- HandleMoveRequestSystem.cs 1개 파일
- FormationUtility, FlowFieldSystem, PredictedMovementSystem 변경 없음

## 리뷰 상태
- 자동 리뷰: 대기 중
