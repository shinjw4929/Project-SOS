# DeathTilt (사망 기울임 연출) 계획 요약

## 문제 정의
엔티티가 HP 0이 되면 1프레임 Dying 후 즉시 파괴되고, ClientDeathSystem이 렌더링을 즉시 숨겨서 사망 연출이 없음.

## Phase 구성
| Phase | 내용 | 주요 변경 |
|---|---|---|
| 1 | 서버 사망 지속시간 도입 | DeathTimer 컴포넌트, ServerDeathSystem 2-Job 분리, PredictedMovementSystem Dying 스킵 |
| 2 | 클라이언트 사망 기울임 연출 | CombatTiltSystem → EntityTiltSystem 리네임 + Dying 기울임, ClientDeathSystem DisableRendering 스킵 |

## 예상 영향 범위
- **서버**: ServerDeathSystem, PredictedMovementSystem
- **클라이언트**: CombatTiltSystem → EntityTiltSystem (리네임), ClientDeathSystem
- **공용**: GameSettings (+3 필드), GameSettingsAuthoring, DeathTimer 신규 컴포넌트
- **기존 동작 변경**: Dying 엔티티가 DeathDuration(기본 0.5초)만큼 지속 후 파괴

## 자동 리뷰 통과 여부
1회차에 승인. 경로/최적화 수준의 경미한 수정 3건 직접 반영 완료.
