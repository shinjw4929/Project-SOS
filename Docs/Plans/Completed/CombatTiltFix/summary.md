# CombatTiltFix 요약

## 문제 정의
1. EnemyBig 공격 시 기울기(CombatTilt)가 시각적으로 적용되지 않음 — lerp 기반 pitch 축적이 Ghost 동기화로 매 프레임 리셋
2. 아군 유닛 공격 기울기가 공격 타이밍과 동기화되지 않음 — Attacking 상태 동안 고정 각도 유지

## Phase 구성
- **Phase 1**: CombatTiltTimer 컴포넌트 추가 + CombatTiltSystem을 시간 기반 swing-return 사이클로 리팩토링 + GameSettings에 SwingRatio 파라미터 추가

## 예상 영향 범위
- `CombatTiltSystem.cs` (Client) — 전면 리팩토링
- `GameSettings.cs` / `GameSettingsAuthoring.cs` (Shared/Authoring) — 필드 1개 추가
- `CombatTiltTimer.cs` (Client) — 신규 컴포넌트
- 서버 코드 변경 없음, Ghost 동기화 변경 없음

## 자동 리뷰
1회차에 승인 (수정 1건: CombatStats Lookup 검토 항목 통합)
