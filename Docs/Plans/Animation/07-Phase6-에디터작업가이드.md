# Phase 6: 에디터 작업 가이드

**전제 조건**: Phase 1-5 코드 구현 완료, Unity 컴파일 에러 없음

---

## 1단계: Burst 캐시 정리

새 타입 추가 후 Burst 해시 캐시가 무효화될 수 있다.

- **방법 A**: Unity Editor > Jobs > Burst > Clear Cache
- **방법 B**: `Library/BurstCache/` 폴더 삭제 후 Unity 재시작

---

## 2단계: VAT 베이킹 (Tools > VAT Baker)

4개 모델을 각각 베이킹한다. **클립 순서가 서버 매핑(`VATAnimationStateUpdateSystem`)과 반드시 일치해야 한다.**

베이킹 시 FBX 내 복수 SkinnedMeshRenderer(SMR)는 자동 병합된다. 서브메시별 VAT 머티리얼이 자동 생성되며, 원본 머티리얼의 텍스처/색상이 자동 복사된다.

### Hero (LittleSquirrel)

1. **Tools > VAT Baker** 메뉴 열기
2. FBX Prefab: `Assets/Download/LittleFriends-CartoonAnimals-Lite/LittleSquirrel.fbx` 드래그
3. Model Name: `Hero`
4. 클립 선택 (나머지 비활성화):

| 순서 | 클립명 | Loop | 서버 매핑 |
|------|--------|------|----------|
| 0 | `02_Idle` | ON | Action.Idle |
| 1 | `03_Walk` | ON | Action.Moving |
| 2 | `07_Eat` | ON | Action.Working |
| 3 | `09_Sleep02` | **OFF** | Action.Dying, Dead |

5. **Bake VAT** 클릭

> Attack 클립 부재 -> Idle(0) 폴백. 전투 연출은 CombatTiltSystem이 처리. Sleep02를 사망 애니메이션으로 활용.

> SMR 5개(Body, Ears, Eyebrows, EyelidsTail, Eyes) 병합 → 서브메시 5개 → VAT 머티리얼 5개 자동 생성 (`Hero_VAT_0.mat` ~ `Hero_VAT_4.mat`).

### EnemySmall (Ghost)

1. FBX Prefab: `Assets/Download/GhostCharacter_Free/Ghost_animation.fbx` 드래그
2. Model Name: `EnemySmall`
3. 클립 선택:

| 순서 | 클립명 | Loop | 서버 매핑 |
|------|--------|------|----------|
| 0 | `ghost_idle` | ON | EnemyContext.Idle, Dormant, Disabled |
| 1 | `ghost_run` | ON | EnemyContext.Wandering, Chasing |
| 2 | `ghost_attack` | OFF | EnemyContext.Attacking |
| 3 | `ghost_dissolve` | OFF | EnemyContext.Dying, Dead |

4. **Bake VAT** 클릭

> 원본 Ghost 머티리얼은 커스텀 셰이더(형광/그라데이션) 사용. 텍스처는 자동 복사되지만, 색상/효과는 수동 보정 필요. 베이킹 후 머티리얼 설정 참조: [머티리얼 수동 보정](#머티리얼-수동-보정-ghost-적).

### EnemyFlying (Ghost -- 동일 FBX)

1. FBX Prefab: `Ghost_animation.fbx` (EnemySmall과 동일)
2. Model Name: `EnemyFlying`
3. 클립/순서: EnemySmall과 동일
4. **Bake VAT** 클릭

### EnemyBig (Little_Ghost_ZOMBANGEL)

1. FBX Prefab: `Assets/Download/Monster_Ghosts_FREE/Little_Ghost_ZOMBANGEL.fbx` 드래그
2. Model Name: `EnemyBig`
3. 클립 선택:

| 순서 | 클립명 | Loop | 서버 매핑 |
|------|--------|------|----------|
| 0 | `idle_up_down` | ON | 모든 상태 (1클립이므로 math.clamp 자동 클램프) |

4. **Bake VAT** 클릭

### 출력물 확인

각 `Assets/VATData/{모델명}/` 폴더에 파일이 생성되어야 한다.
SMR이 복수인 FBX는 서브메시별 머티리얼이 생성된다 (`*_VAT_0.mat`, `*_VAT_1.mat`, ...).
SMR 1개인 FBX는 단일 머티리얼 (`*_VAT.mat`).

```
Assets/VATData/Hero/
├── Hero_Positions.asset          # Position Texture (RGBAHalf)
├── Hero_BindPose.asset           # Static Mesh (5개 SMR 병합, 서브메시 5개, UV2 인코딩)
├── Hero_ClipData.asset           # VATClipDataAsset (ScriptableObject)
├── Hero_VAT_0.mat                # Body (Squirrel_Body_Idle 텍스처)
├── Hero_VAT_1.mat                # Ears (Squirrel_Ears 텍스처)
├── Hero_VAT_2.mat                # Eyebrows (Eyebrows 텍스처)
├── Hero_VAT_3.mat                # EyelidsTail (Squirrel_EyelidsTail 텍스처)
└── Hero_VAT_4.mat                # Eyes (Eyes 텍스처)

Assets/VATData/EnemySmall/
├── EnemySmall_Positions.asset
├── EnemySmall_BindPose.asset
├── EnemySmall_ClipData.asset
└── EnemySmall_VAT.mat            # 단일 머티리얼 (texture_ghost.png)

Assets/VATData/EnemyFlying/
├── EnemyFlying_Positions.asset
├── EnemyFlying_BindPose.asset
├── EnemyFlying_ClipData.asset
└── EnemyFlying_VAT.mat

Assets/VATData/EnemyBig/
├── EnemyBig_Positions.asset
├── EnemyBig_BindPose.asset
├── EnemyBig_ClipData.asset
└── EnemyBig_VAT.mat
```

---

## 3단계: 프리팹 설정 (VAT 적용 4개)

### 프리팹 구조

모든 프리팹은 동일한 구조:
```
루트 (Authoring, Collider 등)     ← VATAnimationAuthoring 추가
└── 자식 (MeshFilter, MeshRenderer)  ← 메시/머티리얼 교체
```

| 프리팹 경로 | 루트 | 자식 (메시) |
|------------|------|-----------|
| `Assets/Prefabs/Units/Hero.prefab` | `Hero` | `LittleSquirrel_Happy` |
| `Assets/Prefabs/Enemy/EnemySmall.prefab` | `EnemySmall` | `EnemySmallModel` |
| `Assets/Prefabs/Enemy/EnemyFlying.prefab` | `EnemyFlying` | `EnemyFlyingModel` |
| `Assets/Prefabs/Enemy/EnemyBig.prefab` | `EnemyBig` | `Little_Ghost_ZOMbi (1)` |

### 3-1. 루트 GameObject에 Authoring 추가

프리팹 더블클릭 → Prefab Mode 진입 → **루트** 선택:

1. **Add Component > VATAnimationAuthoring**
2. Inspector의 `ClipData` 슬롯에 해당 `*_ClipData.asset` 할당

### 3-2. 자식 메시 교체

#### 다중 SMR 모델 (Hero)

원본 `LittleSquirrel_Happy` 프리팹 내부에 Body, Ears, Eyebrows, EyelidsTail, Eyes 등 여러 자식 GameObject가 있다. BindPose가 이들을 하나로 병합했으므로:

1. **Body** (또는 첫 번째 메시 자식) 선택
2. **MeshFilter** > Mesh를 `Hero_BindPose.asset`으로 교체
3. **MeshRenderer** > Materials **Size를 5**로 변경 (숫자 직접 입력)
4. Element 0~4에 `Hero_VAT_0.mat` ~ `Hero_VAT_4.mat` 순서대로 할당
5. **나머지 자식 (Ears, Eyebrows, EyelidsTail, Eyes)를 삭제 또는 비활성화** (병합 메시에 이미 포함)

#### 단일 SMR 모델 (EnemySmall, EnemyFlying, EnemyBig)

1. 자식 메시 GameObject 선택
2. **MeshFilter** > Mesh를 `*_BindPose.asset`으로 교체
3. **MeshRenderer** > Material을 `*_VAT.mat`으로 교체

### 프리팹별 할당 요약

| 프리팹 | ClipData | BindPose Mesh | VAT Material |
|--------|----------|---------------|-------------|
| Hero | `Hero_ClipData.asset` | `Hero_BindPose.asset` | `Hero_VAT_0~4.mat` (5개, Size=5) |
| EnemySmall | `EnemySmall_ClipData.asset` | `EnemySmall_BindPose.asset` | `EnemySmall_VAT.mat` |
| EnemyFlying | `EnemyFlying_ClipData.asset` | `EnemyFlying_BindPose.asset` | `EnemyFlying_VAT.mat` |
| EnemyBig | `EnemyBig_ClipData.asset` | `EnemyBig_BindPose.asset` | `EnemyBig_VAT.mat` |

### VAT 미적용 프리팹 (4개) -- 변경 없음

Worker, Striker, Tank, Archer는 기존 정적 메시/머티리얼을 유지한다.
CombatTiltSystem만 적용됨 (Attacking 상태 시 기울임).

---

## 머티리얼 수동 보정 (Ghost 적)

Ghost 원본 머티리얼(`MaterialGhost.mat`)은 커스텀 셰이더(형광/그라데이션/Dissolve)를 사용한다. VAT 셰이더는 기본 PBR이므로 완전 재현은 불가하나, 아래 설정으로 근사할 수 있다.

### 원본 참조값

| 프로퍼티 | RGB (0~255) | 비고 |
|----------|-------------|------|
| `_MainColor` | `(199, 74, 255)` | 밝은 보라 |
| `_Color` | `(166, 0, 181)` | 진한 보라 |
| `_MainTexture` | `texture_ghost.png` | |

### EnemySmall/EnemyFlying VAT 머티리얼 설정

| 프로퍼티 | 설정값 (RGB 0~255) | 비고 |
|----------|-------------------|------|
| **Base Map** | `texture_ghost.png` | 자동 복사됨, 없으면 수동 할당 |
| **Base Color** | `(166, 0, 181, 255)` 진한 보라 | 수동 설정 |
| **Emission Color** (HDR) | `(77, 26, 128)` 정도 | 형광 느낌 근사, 강도 조절 |
| **Alpha Clip** | 필요 시 체크 | Ghost 투명 부분이 있으면 활성화 |
| **Alpha Cutoff** | `0.1` ~ `0.5` | Alpha Clip 활성화 시 조절 |

### VAT 셰이더 추가 프로퍼티 (Inspector)

| 프로퍼티 | 용도 | 기본값 |
|----------|------|--------|
| **Emission Color** (HDR) | 자체 발광 색상. 형광/글로우 효과 | `(0,0,0,0)` = 비활성 |
| **Emission Map** | 발광 텍스처 (선택) | 없음 |
| **Alpha Clip** (토글) | Alpha Cutout 활성화 | OFF |
| **Alpha Cutoff** | 컷오프 임계값. 이하의 alpha 픽셀 완전 투명 | 0.5 |

---

## 4단계: SoundManager 배치

1. 씬(일반 씬, SubScene 아닌 곳)에 빈 GameObject 생성
2. 이름: `SoundManager`
3. **Add Component > SoundManager**
4. Inspector에서 AudioClip 슬롯 할당 (에셋 확보 후)
   - Melee Hit Clip, Ranged Shot Clip, Unit Death Clip, Enemy Death Clip, Worker Gather Clip 등
   - 에셋 미확보 시 비워둬도 시스템 자체는 동작 (사운드만 안 남)

---

## 5단계: 검증

### VAT 애니메이션 확인

| 대상 | 테스트 시나리오 | 기대 결과 |
|------|---------------|----------|
| Hero | Idle -> 이동 명령 | 걷기 애니메이션 재생 |
| Hero | 자원 채집 시작 | Eat 애니메이션 재생 |
| Hero | 공격 상태 | Idle 유지 + 전방 기울임 |
| Hero | 사망 | Sleep02 애니메이션 재생 (ClientDeathSystem 딜레이 필요 시 별도 수정) |
| EnemySmall | 스폰 후 배회 | 달리기 애니메이션 재생 |
| EnemySmall | 유닛 발견 -> 추격 | 달리기 유지 |
| EnemySmall | 공격 시작 | 공격 애니메이션 재생 + 기울임 |
| EnemyFlying | EnemySmall과 동일 동작 확인 | 동일 |
| EnemyBig | 스폰 후 모든 상태 | idle_up_down 떠다니기 애니메이션 (모든 상태 동일) |

### 비VAT 유닛 기울임 확인

| 대상 | 테스트 시나리오 | 기대 결과 |
|------|---------------|----------|
| Worker/Striker/Tank/Archer | 공격 상태 진입 | 전방 기울임 동작 |
| Worker/Striker/Tank/Archer | 공격 종료 | 기울임 해제 (부드럽게 복귀) |

### 문제 발생 시 체크포인트

1. **자홍색(Magenta)**: 셰이더 컴파일 에러. Console에서 셰이더 에러 확인
2. **메시가 안 보임**: VAT Material의 `_VATPositionTex` Inspector에서 텍스처 할당 확인
3. **메시가 뒤틀림**: `_VATTexelSize` 값 확인 (x = 1/버텍스수, y = 1/총프레임수)
4. **메시가 흰색/회색**: Base Map 텍스처 미할당. 수동으로 원본 텍스처 드래그
5. **다중 SMR 모델에서 일부만 보임**: MeshRenderer Materials Size가 서브메시 수와 일치하는지 확인. 나머지 자식 삭제/비활성화 확인
6. **애니메이션 안 바뀜**: VATAnimationAuthoring의 ClipData 할당 확인, 서버 월드에서 VATAnimationState 컴포넌트 존재 확인
7. **엉뚱한 애니메이션**: 베이킹 시 클립 순서와 서버 매핑 인덱스 일치 여부 확인
8. **기울임 안 됨**: GameSettings Inspector에서 CombatTiltAngle(기본 0.3), CombatTiltSpeed(기본 8.0) 값 확인
9. **TeamColor 미적용**: VAT Material에 `_BaseColor` 프로퍼티가 있는지 확인 (셰이더에 포함되어 있음)
10. **SRP Batcher 경고**: CBUFFER의 모든 변수가 Properties에 선언되어 있는지 확인

---

## 6단계: 성능 프로파일링 (선택)

500+ 유닛 스폰 후:

1. **Profiler > GPU**: VAT 셰이더 렌더링 비용 확인
2. **Profiler > CPU**: VATAnimationPlaybackSystem, CombatTiltSystem 소요 시간 확인
3. **Profiler > Memory**: Position Texture 메모리 사용량 확인
4. 사운드 동시 재생 시 AudioSource 풀 포화 여부 확인
