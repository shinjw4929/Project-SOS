# BGM 시스템 오케스트레이션 플랜

## 문제 정의
- 현재 게임에 BGM이 전혀 없어 모든 화면이 무음 상태 (SFX만 존재)
- 시작화면(Title), 로비(Lobby/InRoom), 인게임(InGame) 세 화면에서 각각 다른 BGM이 필요
- 영향 범위: 클라이언트 전용. 기존 SFX 시스템(SoundManager, SoundEventEmitSystem)에 영향 없음

## AS-IS (현재 상태)

### 사운드 시스템 구조
- `SoundEventEmitSystem` (ECS, Client) → `SoundEvent` 버퍼 → `SoundManager` (MonoBehaviour) 패턴
- SFX 전용: 32개 AudioSource 풀, 3D 공간 음향, 타입별 동시재생 제한
- BGM 관련 코드/컴포넌트 전무

### 게임 상태 전환 흐름
```
RoomClientState.Disconnected  →  titlePanel (시작화면, TimeScale=0)
        ↓ [게임시작 버튼]
RoomClientState.Connecting    →  connectingPanel
        ↓
RoomClientState.Lobby         →  lobbyPanel (방 목록)
        ↓ [방 생성/입장]
RoomClientState.InRoom        →  roomPanel (대기실)
        ↓ [GameStart 수신]
RoomClientState.Matched       →  gameStartingPanel
        ↓
RoomClientState.InGame        →  UI 비활성화, TimeScale=1, ECS 게임 시뮬레이션
        ↓ [게임 종료]
RoomClientState.Disconnected  →  titlePanel (로비 복귀)
```

### 관련 파일
| 파일 | 역할 |
|---|---|
| `Assets/Scripts/Client/Controller/Sound/SoundManager.cs` | SFX 재생 (MonoBehaviour, AudioSource 풀) |
| `Assets/Scripts/Client/Component/Sound/SoundEvent.cs` | SFX 이벤트 버퍼 (IBufferElementData) |
| `Assets/Scripts/Client/Systems/Sound/SoundEventEmitSystem.cs` | ECS 상태변화 → SoundEvent 발행 |
| `Assets/Scripts/Client/Network/RoomClient.cs` | 룸 서버 TCP 클라이언트, RoomClientState 상태머신 |
| `Assets/Scripts/Client/Controller/UI/RoomUIController.cs` | UI 패널 전환, RoomClient 이벤트 구독 |
| `Assets/Scripts/Shared/Components/Sound/SoundType.cs` | SFX 타입 enum |

## TO-BE (목표 상태)

### BGM 전환 매핑
| RoomClientState | BGM | 비고 |
|---|---|---|
| Disconnected | Title BGM | 시작화면 |
| Connecting | Title BGM 유지 | 짧은 전환, BGM 변경 불필요 |
| Lobby | Lobby BGM | 방 목록 화면 |
| InRoom | Lobby BGM 유지 | 로비의 연장 |
| Matched | Lobby BGM → InGame BGM 크로스페이드 | 게임 시작 전환 |
| InGame | InGame BGM | 게임 플레이 중 |

### 구현 방식: 순수 MonoBehaviour
BGM은 ECS가 불필요한 영역:
1. 상태 소스가 `RoomClientState`(MonoBehaviour) — ECS 엔티티 상태가 아님
2. 공간 음향 불필요 (2D 오디오)
3. 동시에 1트랙만 재생 — 풀링 불필요

→ `BGMManager` MonoBehaviour 1개로 해결. `RoomClient.OnStateChanged` 구독.

### 추가/변경 항목
| 구분 | 항목 | 설명 |
|---|---|---|
| **신규** | `BGMManager.cs` | MonoBehaviour. 2개 AudioSource로 크로스페이드. RoomClient.OnStateChanged 구독 |
| **변경 없음** | `SoundManager.cs` | 기존 SFX 시스템 그대로 유지 |
| **변경 없음** | `SoundEventEmitSystem.cs` | 기존 ECS 이벤트 시스템 그대로 유지 |
| **변경 없음** | `RoomClient.cs` | 이벤트만 구독, 코드 변경 없음 |

## AS-IS vs TO-BE 비교표
| 항목 | AS-IS | TO-BE |
|---|---|---|
| BGM 유무 | 없음 (전 화면 무음) | 시작/로비/인게임 각각 다른 BGM |
| 사운드 시스템 | SFX 전용 (SoundManager) | SFX (SoundManager) + BGM (BGMManager) 공존 |
| 상태 감지 | ECS 엔티티 상태 (UnitActionState 등) | RoomClientState (MonoBehaviour 이벤트) |
| AudioSource | 32개 풀 (3D, 라운드로빈) | 2개 고정 (2D, 크로스페이드) |
| BGM 전환 | N/A | 크로스페이드 (Inspector 설정 가능) |

## Phase 체크리스트

### Phase 1: BGMManager 구현
- [ ] `BGMManager.cs` MonoBehaviour 생성
- [ ] 2-AudioSource 크로스페이드 로직 구현
- [ ] RoomClientState 기반 BGM 전환 로직 구현
- [ ] Inspector 설정 (AudioClip 3종, 볼륨, 페이드 시간)
→ 상세: [phase-1-bgm-manager.md](./phase-1-bgm-manager.md)

## Phase 간 의존성
| Phase | 의존성 | 병렬 가능 |
|---|---|---|
| 1 | 없음 | - |

## 변경 파일 요약
| Phase | 파일 | 변경 |
|---|---|---|
| 1 | `Assets/Scripts/Client/Controller/Sound/BGMManager.cs` | 신규 생성 |

## 검증 방법
1. 컴파일 성공 확인
2. 시작화면에서 Title BGM 재생 확인
3. 로비 진입 시 Lobby BGM으로 크로스페이드 확인
4. 게임 시작 시 InGame BGM으로 크로스페이드 확인
5. 게임 종료 → 로비 복귀 시 Title/Lobby BGM 복원 확인
6. BGM과 SFX가 동시에 정상 재생되는지 확인

## 롤백 전략
- Phase 1: `BGMManager.cs` 파일 삭제 + 씬에서 컴포넌트 제거. 기존 시스템에 변경 없으므로 완전 복원 가능.
