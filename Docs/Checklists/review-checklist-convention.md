# 컨벤션 / 코드 품질 리뷰 체크리스트

> `/review-code` 스킬의 컨벤션 에이전트, `/review-plan` 스킬이 참조하는 검토 항목.

## C. 프로젝트 컨벤션

| 검토 항목 | 위반 조건 |
|-----------|-----------|
| 네이밍 - User | "Player" 사용 (Unity Player와 혼동, "User" 사용해야 함) |
| 네이밍 - 변수명 | 의미 불명의 축약 변수명 (`var c`, `var t` 등). 단, `ecb`, `em`, `job` 등 DOTS 관용어는 허용 |
| 네이밍 - 파일명 | 폴더별 네이밍 패턴 불일치 (Commands → `*InputSystem.cs`, RPCs → `*Rpc.cs` 등) |
| GameSettings 패턴 | 밸런스/규칙 상수를 시스템 코드에 직접 작성 (GameSettings 미사용). Job 내부에서 GameSettings를 직접 읽는 경우도 위반 (OnUpdate에서 읽어 구조체 필드로 전달해야 함) |
| 중복 구현 | 기존 유틸리티(ArrivalUtility, CombatUtility, SpatialMaps 등)를 재구현 |
| Work Range 패턴 | 작업 거리를 인라인 계산. `ArrivalUtility.GetInteractionArrivalDistance`/`CombatUtility` 사용해야 함 |
| 싱글톤 중복 | 기존 싱글톤을 확장할 수 있는데 새 싱글톤 생성 |
| Authoring 패턴 | Authoring 조합 불일치 (유닛: `Movement`+`UnitMovement`+`Unit`, 적: `Movement`+`Enemy`, 건물: `Structure`) |
| 기존 패턴 일관성 | 같은 유형의 기존 코드(같은 SystemGroup 내 시스템, 같은 폴더 내 컴포넌트 등)와 구조(어트리뷰트 배치, 필드 순서, 메서드 구성, ECB/Query 패턴)가 일치하지 않는 경우 |

## E. 코드 품질

| 검토 항목 | 위반 조건 |
|-----------|-----------|
| 보안 | 커맨드 인젝션, 검증 없는 외부 입력 처리 |
| Structural Change | Structural Change를 루프/Job 내에서 반복 (ECB로 지연 처리해야 함) |
| 엣지 케이스 | 엔티티 파괴, 연결 끊김, null Entity 미처리 |
