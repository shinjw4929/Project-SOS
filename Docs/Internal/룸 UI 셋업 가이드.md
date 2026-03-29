# 룸 UI 셋업 가이드

RoomUIController가 Inspector에서 참조하는 UI 요소들의 생성 및 연결 절차.

---

## 1. RoomClient 오브젝트 생성

```
Hierarchy → 빈 GameObject → 이름: "RoomClient"
  → Add Component: RoomClient
```

DontDestroyOnLoad로 자동 유지된다. 씬에 1개만 배치.

---

## 2. RoomScreen Canvas 계층 구조

```
Hierarchy → UI → Canvas 생성 → 이름: "RoomScreen"
```

하위에 5개 Panel을 생성한다:

```
RoomScreen (Canvas)
├── ConnectingPanel                 ← "연결 중..." 텍스트 + 스피너
├── LobbyPanel                      ← 방 목록 화면
│   ├── TitleText (TMP)
│   ├── UserNameInput (TMP_InputField)
│   ├── RoomNameInput (TMP_InputField)
│   ├── RoomListScrollView (ScrollRect)
│   │   └── Viewport
│   │       └── Content             ← Inspector: roomListContent
│   ├── CreateRoomButton (Button)
│   └── RefreshButton (Button)
├── RoomPanel                        ← 대기실 화면
│   ├── RoomTitleText (TMP)
│   ├── UserListScrollView (ScrollRect)
│   │   └── Viewport
│   │       └── Content             ← Inspector: userListContent
│   ├── ReadyButton (Button + TMP 자식)
│   ├── LeaveButton (Button)
│   └── StartGameButton (Button)    ← 호스트에게만 표시
├── GameStartingPanel               ← "게임 서버 접속 중..." 텍스트
└── ErrorPanel
    ├── ErrorMessageText (TMP)
    └── RetryButton (Button)
```

`RoomScreen` Canvas에 `RoomUIController` 컴포넌트를 추가한다.

---

## 3. 아이템 프리팹 (Assets/Prefabs/UI/)

### RoomListItem.prefab (방 목록 항목)

```
RoomListItem (HorizontalLayoutGroup)
├── RoomNameText (TMP)          ← 방 이름
├── PlayerCountText (TMP)       ← "3/4"
├── HostNameText (TMP)          ← 호스트 이름
└── JoinButton (Button)         ← 입장
```

### UserListItem.prefab (대기실 유저 항목)

```
UserListItem (HorizontalLayoutGroup)
├── UserNameText (TMP)          ← 유저 이름
├── HostBadge (TMP)             ← "[HOST]" (호스트일 때만 표시)
└── ReadyStatusText (TMP)       ← "READY" / ""
```

두 프리팹 모두 `Assets/Prefabs/UI/`에 저장한다.

---

## 4. Inspector 필드 연결

RoomScreen Canvas의 RoomUIController 컴포넌트에서 각 필드를 드래그앤드롭으로 연결한다:

| Inspector 필드 | 연결 대상 |
|---------------|-----------|
| Room Client | RoomClient 게임오브젝트 |
| Connecting Panel | ConnectingPanel |
| Lobby Panel | LobbyPanel |
| Room Panel | RoomPanel |
| Game Starting Panel | GameStartingPanel |
| Error Panel | ErrorPanel |
| Room List Content | LobbyPanel > ScrollView > Viewport > Content |
| Room List Item Prefab | Assets/Prefabs/UI/RoomListItem.prefab |
| Room Name Input | LobbyPanel > RoomNameInput |
| User Name Input | LobbyPanel > UserNameInput |
| Create Room Button | LobbyPanel > CreateRoomButton |
| Refresh Button | LobbyPanel > RefreshButton |
| Room Title Text | RoomPanel > RoomTitleText |
| User List Content | RoomPanel > ScrollView > Viewport > Content |
| User List Item Prefab | Assets/Prefabs/UI/UserListItem.prefab |
| Ready Button | RoomPanel > ReadyButton |
| Leave Button | RoomPanel > LeaveButton |
| Start Game Button | RoomPanel > StartGameButton |
| Ready Button Text | ReadyButton 자식 TMP |
| Error Message Text | ErrorPanel > ErrorMessageText |
| Retry Button | ErrorPanel > RetryButton |

---

## 5. 접속 시작 트리거

씬 로드 시 자동 접속:

```csharp
// 씬의 초기화 스크립트 또는 별도 bootstrap에서
roomClient.ConnectToRoomServer("127.0.0.1", 8080);
```

또는 `RoomUIController.SetConnectionInfo(host, port)` 호출 후 접속 버튼을 추가하여 유저가 직접 트리거하는 방식도 가능.

---

## 주의사항

- `PopulateRoomList`, `PopulateUserList`는 프리팹 자식에서 `GetComponentInChildren<TextMeshProUGUI>()`로 텍스트를 찾는다. 프리팹 내부 구조가 달라지면 동작하지 않을 수 있다.
- 프리팹에 전용 컴포넌트(`RoomListItemView` 등)를 붙여서 참조를 명시적으로 관리하는 것이 더 안정적이다.
- 프리팹 생성 후 반드시 동작 테스트 필요.
