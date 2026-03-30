# 사운드 에셋 통합 + VAT 미해결 이슈

## Context

VAT 애니메이션 + 사운드 시스템 코드 구현이 완료되었으나 (Animation 계획 Phase 1~5), 사운드 에셋 확보 전이므로 에셋 통합 및 통합 테스트가 미실행 상태이다. 또한 VAT 셰이더 관련 미해결 이슈가 존재한다.

**전제 조건**: 사운드 파일(AudioClip) 확보

**원본 계획**: `Docs/Plans/Completed/Animation/`

---

## Phase 체크리스트

### Phase 1: 사운드 에셋 통합 + 튜닝

- [x] 코드 수정: 적 공격 사운드 분리
  - SoundType enum에 `EnemyMeleeHit = 14`, `EnemyRangedShot = 15` 추가
  - SoundEventEmitSystem.GetEnemySoundType: Attacking → `EnemyMeleeHit`/`EnemyRangedShot` 반환하도록 수정
  - SoundManager에 EnemyMeleeHit, EnemyRangedShot AudioClip 필드 추가
- [ ] SoundManager Inspector에 SoundType → AudioClip 매핑 할당 (9개 clip 필드)
- [ ] 자동 발생 SoundType 7개 재생 검증 + 볼륨 기본값 설정: MeleeHit, RangedShot, EnemyMeleeHit, EnemyRangedShot, UnitDeath, EnemyDeath, WorkerGather
  - BuildingPlace/BuildingComplete/MoveCommand는 SoundEventEmitSystem에서 발생하지 않음 (입력/건설 시스템에서 수동 추가 필요, 본 계획 범위 외)
- [ ] 파라미터 튜닝: AudioSource 풀 크기(32), 카메라 컬링 거리(80m), 동일 타입 동시 재생 제한(3개)

### Phase 2: VAT + 사운드 통합 테스트

- [ ] VAT 유닛 테스트: Hero Idle→Moving→Working (걷기 VAT), Attacking (기울임 폴백)
- [ ] VAT 적 테스트: EnemySmall Idle→Moving→Attacking→Dying (VAT + 기울임)
- [ ] VAT 적 테스트: EnemyFlying — EnemySmall과 동일 동작 확인 (같은 FBX)
- [ ] 비VAT 유닛 테스트: Worker/Striker/Tank/Archer Idle→Working→Attacking (기울임 + 사운드)
- [ ] 비VAT 적 테스트: EnemyBig Idle→Attacking (기울임 + 사운드)
- [ ] 500+ 유닛 전투 시 사운드 성능 프로파일링
  - 목표: SoundEventEmitSystem < 0.5ms, SoundManager.Update() < 1ms
- [ ] Dying/Dead 상태 애니메이션 재생 여부 확인 (ClientDeathSystem이 Health<=0에서 DisableRendering 즉시 추가하므로 사망 애니메이션 미표시 예상. 수정이 필요하면 이슈 6 선행 해결 필요)

---

## 미해결 이슈 (별도 작업 가능)

원본: `Docs/Plans/Completed/Animation/08-미해결이슈.md`

### 이슈 5: Ghost 적 공격 애니메이션 타이밍 불일치
- **상태**: 미착수 / **우선순위**: 중간
- EnemySmall/EnemyFlying ghost_attack 클립 재생 시간과 공격 쿨타임 불일치
- 해결 방안: ClipData Fps 조절 (Inspector) 또는 AnimStartTime 리셋 (코드)

### 이슈 6: ClientDeathSystem과 사망 애니메이션 충돌
- **상태**: 인지 / **우선순위**: 중간
- ClientDeathSystem이 DisableRendering 즉시 추가 → 사망 애니메이션 미표시
- 해결 방안: 사망 딜레이 타이머 추가

### 이슈 1/2: VAT 셰이더 Emission + Alpha Cutout (완료)
- 이미 구현 완료. 참고용으로만 기록.

### 이슈 3: VATBakeUtility 텍스처 자동 복사 한계 (인지)
- 커스텀 셰이더 색상 프로퍼티 자동 복사 안 됨. 수동 보정 필요.

---

## 참조

- `Docs/Plans/Completed/Animation/06-Phase6-사운드에셋및문서.md` — 상세 에셋 통합 절차
- `Docs/Plans/Completed/Animation/08-미해결이슈.md` — 미해결 이슈 상세
- `Assets/Scripts/Client/Controller/Sound/SoundManager.cs` — SoundManager 구현
- `Assets/Scripts/Client/Systems/Sound/SoundEventEmitSystem.cs` — 사운드 이벤트 시스템
