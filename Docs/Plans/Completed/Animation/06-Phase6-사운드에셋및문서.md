# Phase 6: 사운드 에셋 통합 + 문서 업데이트

**전제 조건**: Phase 1~5 구현 완료 + 사운드 파일 확보

---

## 사운드 에셋 통합

### SoundManager 설정

SoundManager Inspector에서 SoundType → AudioClip 매핑 할당:

| SoundType | AudioClip 파일 | 볼륨 기본값 |
|-----------|---------------|-----------|
| MeleeHit | `Audio/SFX/melee_hit.wav` | 1.0 |
| RangedShot | `Audio/SFX/ranged_shot.wav` | 0.8 |
| UnitDeath | `Audio/SFX/unit_death.wav` | 1.0 |
| EnemyDeath | `Audio/SFX/enemy_death.wav` | 1.0 |
| WorkerGather | `Audio/SFX/gather.wav` | 0.5 |
| BuildingPlace | `Audio/SFX/building_place.wav` | 0.7 |
| BuildingComplete | `Audio/SFX/building_complete.wav` | 0.8 |
| MoveCommand | `Audio/SFX/move_command.wav` | 0.6 |

> **MoveCommand/BuildingPlace/BuildingComplete**: SoundEventEmitSystem(엔티티 상태 변화 기반)으로는 발생하지 않음. 입력 시스템/건설 시스템에서 SoundEvent 버퍼에 직접 추가해야 하며, Phase 6 범위 외. SoundType enum에 미리 정의만 해둔다.

### 파라미터 튜닝

| 파라미터 | 기본값 | 조정 범위 | 비고 |
|---------|-------|----------|------|
| AudioSource 풀 크기 | 32 | 16~64 | 프로파일링 후 조정 |
| 카메라 컬링 거리 | 80m | 50~120m | 카메라 줌 레벨에 따라 |
| 동일 타입 동시 재생 제한 | 3개 | 1~5 | 대규모 전투 시 사운드 과포화 방지 |
| 전체 볼륨 | 1.0 | 0~1 | AudioListener 기준 |

---

## 문서 업데이트

### Architecture.md

**변경 위치**: `Docs/Architecture.md`

1. **System Execution Flow 섹션** — 기존 번호 사이에 삽입:
   ```
   [6] 전투 (FixedStepSimulationSystemGroup) 다음에 삽입:

   [6.5. 애니메이션] SimulationSystemGroup (Server, UpdateAfter FixedStepSimulationSystemGroup)
       VATAnimationStateUpdateSystem (유닛+적 클립 인덱스 갱신 → Ghost 동기화)

   [9.5] 커맨드 마커 다음에 삽입:

   [9.7. 애니메이션+사운드] SimulationSystemGroup (Client)
       VATAnimationInitSystem → VATAnimationPlaybackSystem → CombatTiltSystem → SoundEventEmitSystem
       → SoundManager (MonoBehaviour, 매 프레임 버퍼 소비 + AudioSource 풀 재생)
   ```

2. **Key Patterns 섹션** — VAT 애니메이션 패턴 추가:
   ```
   ### N. VAT Animation Pattern
   GPU Animation (Vertex Animation Texture) 방식으로 수천 유닛을 동시 애니메이팅.
   서버: VATAnimationState(Ghost) 갱신 → 클라이언트: VATAnimParam(MaterialProperty) 계산 → 셰이더 버텍스 변형.
   ```

3. **Folder Structure 테이블** — 추가:
   ```
   | `Shared/Components/Animation/` | 애니메이션 상태/클립 | `VATAnimation*.cs` |
   | `Shared/Components/Sound/` | 사운드 타입 정의 | `SoundType.cs` |
   | `Client/Component/Animation/` | 셰이더 파라미터 | `VATAnimParam.cs` |
   | `Client/Component/Sound/` | 사운드 이벤트 버퍼 | `SoundEvent.cs` |
   | `Client/Systems/Sound/` | 사운드 이벤트 | `Sound*System.cs` |
   | `Client/Controller/Sound/` | 사운드 매니저 | `SoundManager.cs` |
   ```

### 코드베이스 구조 문서

**변경 위치**: `Docs/Systems/코드베이스 구조.md`

신규 파일 목록 반영:
- `Editor/VATBaker/` (2개)
- `Shared/Components/Animation/` (3개: VATAnimationState, VATClipLibrary, VATClipDataAsset)
- `Client/Component/Animation/` (3개: VATAnimParam, VATAnimTarget, PreviousClipIndex)
- `Client/Component/State/` (2개: PreviousActionState, PreviousEnemyContext)
- `Client/Component/Sound/` (1개)
- `Client/Systems/Animation/` (3개: VATAnimationInitSystem, VATAnimationPlaybackSystem, CombatTiltSystem)
- `Client/Systems/Sound/` (1개)
- `Client/Controller/Sound/` (1개)
- `Server/Systems/Animation/` (1개)
- `Authoring/Animation/` (1개)
- `Shaders/` (1개)

---

## 검증 체크리스트

- [ ] PreviousActionState/PreviousEnemyContext 초기값 처리 확인 (첫 프레임 불필요 이벤트 없음)
- [ ] SoundEvent.Position이 엔티티 LocalToWorld.Position을 올바르게 사용하는지 확인
- [ ] VAT 적용 유닛(Hero) 상태 사이클: Idle → Moving → Working (걷기 VAT + 사운드), Attacking (기울임 폴백)
- [ ] VAT 미적용 유닛(Worker/Striker/Tank/Archer) 상태 사이클: Idle → Moving → Working → Attacking (기울임 + 사운드, Worker는 Working 채집 포함)
- [ ] VAT 미적용 적(EnemyBig) 상태 사이클: Idle → Attacking (기울임 + 사운드, EnemyState 경로 별도 확인)
- [ ] VAT 적용 적(EnemySmall) 상태 사이클: Idle → Moving → Attacking → Dying (VAT + 사운드)
- [ ] VAT 적용 적(EnemyFlying) 상태 사이클: EnemySmall과 동일 동작 확인 (같은 FBX, 동일 클립)
- [ ] 각 상태 전환 시 SoundEvent 발생 확인 (MeleeHit/RangedShot, UnitDeath, EnemyDeath, WorkerGather)
- [ ] Dying/Dead 상태 애니메이션 재생 여부 확인 (ClientDeathSystem과의 상호작용)
- [ ] 500+ 유닛 전투 시 사운드 성능 프로파일링:
  - 측정 대상: SoundEventEmitSystem CPU 시간, SoundManager.Update() CPU 시간, AudioSource 풀 활용률 (사용 중/전체)
  - 목표: SoundEventEmitSystem < 0.5ms, SoundManager.Update() < 1ms
  - 확인: 사운드 이벤트 과다 발생 시 버퍼 오버플로우 없는지, 동시 재생 제한이 정상 작동하는지

## 에셋 통합 체크리스트

- [ ] SoundManager Inspector에 SoundType → AudioClip 매핑 할당
- [ ] 각 SoundType별 AudioClip 재생 검증
- [ ] 볼륨 기본값 설정 (SoundType별 상대 볼륨)
- [ ] 카메라 컬링 거리 튜닝 (기본 80m, 프로파일링 후)
- [ ] 동일 타입 동시 재생 제한 수 튜닝 (기본 3개)
- [ ] AudioSource 풀 크기 튜닝 (기본 32개, 프로파일링 후)

## 문서 업데이트 체크리스트

- [x] `Docs/Architecture.md` — System Execution Flow에 애니메이션/사운드 추가
- [x] `Docs/Architecture.md` — Key Patterns에 VAT Animation Pattern 추가
- [x] `Docs/Architecture.md` — Folder Structure 테이블에 신규 폴더 추가
- [x] `Docs/Systems/코드베이스 구조.md` — 신규 파일 목록 반영

## Post-Implementation 체크리스트 (CLAUDE.md 준수)

- [x] 문서 업데이트: 위 "문서 업데이트 체크리스트" 항목 전부 완료 확인 (Architecture.md, 코드베이스 구조.md)
- [x] 주석 정합성 점검: Phase 1~5에서 변경된 파일의 기존 주석이 코드 동작과 일치하는지 확인
- [x] CLAUDE.md 동기화: Development Guidelines > Animation & Sound Rules 섹션 추가
- [ ] WorkLog 기록 → execution-log.md에 기록 완료 (계획 기반 작업이므로 WorkLog 생략)
