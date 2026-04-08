# Phase 1: BGMManager 구현

## 목표
- 시작화면/로비/인게임에서 각각 다른 BGM을 재생하는 MonoBehaviour 구현
- 화면 전환 시 크로스페이드로 자연스러운 BGM 전환

## 선행 조건
- 없음 (기존 시스템 변경 없이 독립 추가)

## 작업 목록

### Task 1: BGMManager.cs 생성

파일 경로: `Assets/Scripts/Client/Controller/Sound/BGMManager.cs`

- [ ] `BGMManager` MonoBehaviour 클래스 생성 (namespace: `Client`)
- [ ] Inspector 필드 정의:
  ```
  [Header("BGM Clips")]
  [SerializeField] AudioClip titleBgm;      // 시작화면
  [SerializeField] AudioClip lobbyBgm;      // 로비/대기실
  [SerializeField] AudioClip inGameBgm;     // 인게임

  [Header("Settings")]
  [SerializeField, Range(0f, 1f)] float bgmVolume = 0.5f;
  [SerializeField] float crossfadeDuration = 1.5f;

  [Header("References")]
  [SerializeField] RoomClient roomClient;
  ```
- [ ] Awake에서 `DontDestroyOnLoad(gameObject)` 호출 (RoomClient와 동일 수명 보장)
- [ ] **중복 인스턴스 방지**: Awake에서 이미 BGMManager 인스턴스가 존재하면 `Destroy(gameObject)` 후 return (씬 재로드 시 중복 생성 방지)
- [ ] 2개 AudioSource 초기화 (Awake):
  - `audioSourceA`, `audioSourceB` 자식 GameObject에 생성
  - `loop = true`, `spatialBlend = 0f` (2D), `playOnAwake = false`
  - `ignoreListenerPause = true` (TimeScale=0 환경에서 AudioListener.pause 영향 방지)
- [ ] `activeSource` / `inactiveSource` 참조 관리 (크로스페이드 시 스왑)

### Task 2: RoomClientState 기반 BGM 전환 로직

- [ ] `Start()`에서 `roomClient.OnStateChanged += HandleStateChanged` 구독
- [ ] `OnDestroy()`에서 구독 해제
- [ ] `HandleStateChanged(RoomClientState state)` 구현:
  ```
  Disconnected, Connecting → PlayBgm(titleBgm)
  Lobby, InRoom             → PlayBgm(lobbyBgm)
  Matched, InGame           → PlayBgm(inGameBgm)
  ```
- [ ] `Start()`에서 `roomClient.State`를 읽어 HandleStateChanged와 동일한 매핑 로직으로 초기 BGM 시작 (하드코딩 금지)
- [ ] 같은 클립으로의 전환 요청은 무시 (중복 방지)

> **참고 — 게임오버 시 BGM 전환 한계**: 현재 `GameOverPanelController`는 `Application.Quit()`만 호출하며, 게임오버 시 `OnStateChanged`가 발생하지 않을 수 있음. 추후 게임오버→로비 복귀 흐름이 구현되면 `OnStateChanged`로 자연스럽게 커버됨. 필요 시 `GameOverEvents.OnGameOver` 구독 추가 고려.

### Task 3: 크로스페이드 로직

- [ ] `PlayBgm(AudioClip clip)` 메서드:
  - 현재 재생 중인 클립과 동일하면 return
  - `inactiveSource`에 새 클립 할당 + Play
  - 코루틴으로 `crossfadeDuration` 동안 볼륨 선형 보간
  - 완료 시 `activeSource` Stop, 참조 스왑
- [ ] `CrossfadeCoroutine` 구현:
  - `activeSource.volume`: bgmVolume → 0
  - `inactiveSource.volume`: 0 → bgmVolume
  - `Time.unscaledDeltaTime` 사용 (TimeScale=0인 로비에서도 동작)
- [ ] 크로스페이드 중 새 전환 요청 시 기존 코루틴 중단 + 즉시 새 크로스페이드 시작
- [ ] `StopAllBgm()` 메서드 (필요 시 외부 호출용)

### Task 4: 볼륨 제어

- [ ] `SetVolume(float volume)` public 메서드 (런타임 볼륨 조절용)
- [ ] 볼륨 변경 시 현재 재생 중인 source에 즉시 반영

## 병렬 작업 구성

단일 파일 작업이므로 병렬화 불필요. 순차 실행.

## 씬 배치 지침
- BGMManager는 **RoomUIController와 다른 별도 GameObject**에 배치할 것. RoomUIController는 InGame 진입 시 `gameObject.SetActive(false)`를 호출하므로, 같은 GameObject에 배치하면 InGame BGM이 재생되지 않음.
- RoomClient와 동일한 DontDestroyOnLoad 계열 GameObject에 배치하거나, 별도 DontDestroyOnLoad GameObject를 생성.
- `[SerializeField] RoomClient roomClient` 참조는 같은 씬에서 Inspector로 연결.

## 테스트 요구사항

### 수동 테스트 (PlayMode)
BGM은 AudioClip 에셋 의존성이 강하므로 수동 테스트가 적합:
1. 씬에 BGMManager 별도 GameObject로 추가 + RoomClient 참조 연결
2. Inspector에 임시 AudioClip 할당
3. 시작화면 → 로비 → 인게임 전환 시 BGM 크로스페이드 확인
4. TimeScale=0 상태(로비)에서도 크로스페이드 동작 확인
5. 게임 종료 → 로비 복귀 시 BGM 전환 확인

## 검증 방법
1. 컴파일 성공
2. BGMManager를 씬에 추가하고 RoomClient 참조 연결 가능
3. Inspector에서 AudioClip/볼륨/페이드시간 설정 가능
4. 화면 전환 시 크로스페이드로 BGM 전환 동작

## 문서 업데이트
- [ ] `Docs/Systems/코드베이스 구조.md`의 `Controller/Sound/` 섹션에 BGMManager 항목 추가

## 완료 기준
- [ ] `BGMManager.cs` 생성 및 컴파일 성공
- [ ] DontDestroyOnLoad + 중복 인스턴스 방지 로직 포함
- [ ] RoomClientState 변화에 따라 3종 BGM 전환 동작
- [ ] 크로스페이드가 TimeScale=0에서도 정상 동작
- [ ] 기존 SFX 시스템(SoundManager)과 간섭 없음
