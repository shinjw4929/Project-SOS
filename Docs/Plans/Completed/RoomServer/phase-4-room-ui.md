# Phase 4: 룸 UI

## 목표

방 목록, 방 생성, 대기실 상태를 유저에게 시각적으로 표시하는 UI를 구현한다.

## 선행 조건

- Phase 2 완료 (RoomClient 이벤트 시스템)
- Phase 3과 병렬 진행 가능

---

## 작업 목록

### Task 1: RoomUIController MonoBehaviour 구현

**파일 (신규)**: `Assets/Scripts/Client/Controller/UI/RoomUIController.cs`

- [ ] MonoBehaviour, RoomClient 이벤트 구독
- [ ] 상태별 화면 전환:
  ```
  Connecting   → "룸 서버에 연결 중..." (스피너)
  Lobby        → 방 목록 화면 (방 생성/참가 가능)
  InRoom       → 대기실 화면 (준비/시작/퇴장)
  GameStarting → "게임 서버 접속 중..." (스피너)
  InGame       → UI 숨김 (게임 화면으로 전환)
  Error        → "연결 실패. 재시도하시겠습니까?" + [재시도]
  ```

### Task 2: 방 목록 화면

- [ ] UI 구성:
  ```
  +-------------------------------+
  |      Project-SOS              |
  |  [방 목록]                    |
  |  +---------------------------+|
  |  | My Room    3/4  WAITING   ||
  |  | Room #2    1/8  WAITING   ||
  |  +---------------------------+|
  |   [방 만들기]     [새로고침]  |
  +-------------------------------+
  ```
- [ ] ScrollView + 방 항목 프리팹 (동적 생성)
- [ ] 방 클릭 → JoinRoomRequest 전송
- [ ] [방 만들기] → 방 이름 입력 팝업 → CreateRoomRequest 전송
- [ ] [새로고침] → RoomListRequest 전송
- [ ] RoomListResponse 수신 시 목록 갱신

### Task 3: 대기실 화면

- [ ] UI 구성:
  ```
  +-------------------------------+
  |  방 이름: My Room  (3/4)      |
  |  Player A (호스트)            |
  |  Player B        [준비 완료]  |
  |  Player C        [준비 중...] |
  |   [준비]          [나가기]    |
  |   [게임 시작] (호스트 전용)   |
  +-------------------------------+
  ```
- [ ] 플레이어 목록 (호스트 표시, 준비 상태 표시)
- [ ] [준비] 버튼 → ToggleReadyRequest 전송
- [ ] [나가기] 버튼 → LeaveRoomRequest 전송
- [ ] [게임 시작] 버튼 (호스트만 표시) → StartGameRequest 전송
- [ ] RoomUpdate 수신 시 플레이어 목록/준비 상태 갱신

### Task 4: 에러/거부 표시

- [ ] RejectResponse 수신 시 reason별 메시지 표시:
  | reason | 메시지 |
  |--------|--------|
  | ROOM_FULL | "방이 가득 찼습니다" |
  | ROOM_NOT_FOUND | "방을 찾을 수 없습니다" |
  | NOT_HOST | "호스트만 시작할 수 있습니다" |
  | NOT_ALL_READY | "모든 플레이어가 준비되지 않았습니다" |
  | RATE_LIMITED | "요청이 너무 빠릅니다" |
  | DUPLICATE_PLAYER | "이미 접속 중인 플레이어입니다" |
  | ALREADY_IN_ROOM | "이미 방에 있습니다" |
  | INVALID_REQUEST | "잘못된 요청입니다" |
  | ROOM_CLOSED | "방이 닫혔습니다" |
- [ ] 에러 토스트 또는 모달 팝업 (기존 ToastNotificationController 활용 검토)

### Task 5: UI 프리팹 생성

- [ ] `Assets/Prefabs/UI/RoomScreen.prefab` — 전체 룸 UI 컨테이너
- [ ] Canvas + Panel 구성 (기존 UI 스타일과 통일)
- [ ] 방 목록 아이템 프리팹

---

## 병렬 작업 구성 (subagent 활용)

| Agent | 작업 내용 | 의존성 |
|-------|----------|--------|
| Agent A | Task 1 + 4 (RoomUIController 코어 + 에러 처리) | RoomClient 이벤트 |
| Agent B | Task 2 + 3 (방 목록 + 대기실 UI) | RoomUIController 코어 |
| Main | Task 5 (프리팹) | Task 1~4 완료 후 |

---

## 테스트 요구사항

### PlayMode Test

- 방 목록 화면 표시 + 방 생성 버튼 클릭 → CreateRoomRequest 전송 확인
- 대기실 화면 + 준비 버튼 클릭 → ToggleReadyRequest 전송 확인
- RoomUpdate 수신 → UI 갱신 확인
- RejectResponse 수신 → 에러 메시지 표시 확인

---

## 검증 방법

1. 에디터에서 방 목록 화면이 표시되고 방 생성/참가 가능
2. 대기실에서 플레이어 목록과 준비 상태가 표시됨
3. 호스트 [게임 시작] → 게임 서버 접속 → UI 숨김
4. RejectResponse 수신 시 적절한 에러 메시지 표시

## 완료 기준

- [x] 방 목록 / 방 생성 / 대기실 화면 구현
- [x] RoomClient 이벤트와 UI 바인딩 동작
- [x] 상태별 화면 전환 정상
- [x] 에러/거부 메시지 표시
- [x] 컴파일 성공
