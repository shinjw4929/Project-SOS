# Phase 2: ChatUIController + 입력 시스템 통합

## 목표
- 채팅 UI 구현 (입력 필드, 메시지 표시, 채널 탭)
- 기존 입력 시스템과 충돌 없는 키보드 분리

## 선행 조건
- Phase 1 완료 (ChatClient.cs 이벤트 인터페이스 확정)
- Unity UI(uGUI) + TextMeshPro 사용 가능

## 작업 목록

### Task 1: ChatUIController 기본 구조

- [ ] MonoBehaviour, Canvas 하위 배치
- [ ] `static bool IsChatFocused` 프로퍼티 — 다른 시스템에서 참조
- [ ] Enter 키 토글: 비활성 → 입력 필드 활성화 + 포커스, 활성 → 메시지 전송 + 비활성화
- [ ] ESC 키: 채팅 포커스 해제 (메시지 전송 없이)
- [ ] DontDestroyOnLoad (ChatClient와 동일 생명주기)

### Task 2: 메시지 표시 영역

- [ ] ScrollRect + VerticalLayoutGroup 구성
- [ ] 50개 메시지 링버퍼 (`Queue<ChatMessageEntry>`, 초과 시 dequeue + UI 제거)
- [ ] 메시지 포맷: `[채널] 이름: 내용` (채널별 색상 구분)

| 채널 | 색상 (제안) |
|---|---|
| LOBBY | #CCCCCC (회색) |
| TEAM | #88FF88 (연두) |
| ALL | #FFFFFF (흰색) |
| WHISPER (후순위) | #FF88FF (분홍) |
| SYSTEM | #FFCC00 (노랑) |

- [ ] TMP Rich Text 비활성화 (`richText = false`) — `<` 태그 인젝션 방지
- [ ] 새 메시지 수신 시 자동 스크롤 (단, 사용자가 위로 스크롤한 경우 유지)
- [ ] 히스토리 메시지 (세션 입장 시 20개) 동일 포맷으로 표시

### Task 3: 채널 탭

- [ ] 로비 상태: LOBBY 탭만 표시
- [ ] 인게임 상태: TEAM / ALL 탭 표시 (LOBBY 숨김, WHISPER는 후순위)
- [ ] 탭 전환 시 현재 채널 변경 → ChatClient.SendMessage에 전달할 channel 결정
- [ ] 상태 전환(로비↔인게임)은 ChatClient 이벤트(OnAuthResult)에 연동하여 자동 전환

### Task 4: 입력 필드

- [ ] TMP_InputField, 최대 200 bytes 제한 (UTF-8 기준 클라이언트 사전 차단)
- [ ] 전송 시 입력 필드 초기화
- [ ] 빈 메시지 / 공백만 입력 시 전송 차단
- [ ] (후순위) WHISPER 입력: `/w 이름 메시지` 형식 파싱 → whisperTarget 추출

### Task 5: ChatClient 이벤트 바인딩

- [ ] ChatClient.OnMessageReceived → 메시지 표시 영역에 추가
- [ ] ChatClient.OnSystemMessage → SYSTEM 색상으로 표시
- [ ] ChatClient.OnChatError → 에러 유형별 UI 피드백:

| ChatErrorCode | UI 피드백 |
|---|---|
| RATE_LIMITED | "메시지를 너무 빠르게 보내고 있습니다" 시스템 메시지 |
| MESSAGE_TOO_LONG | "메시지가 너무 깁니다" (클라이언트 사전 차단이 우선) |
| PLAYER_NOT_FOUND | "대상을 찾을 수 없습니다" 시스템 메시지 |
| NOT_AUTHENTICATED | (자동 재인증 시도, UI 표시 불필요) |
| CHANNEL_NOT_JOINED | (로그만, UI 표시 불필요) |

- [ ] ChatClient.OnDisconnected → "서버 연결이 끊어졌습니다" 시스템 메시지
- [ ] ChatClient.OnConnected → "채팅 서버에 연결되었습니다" 시스템 메시지

### Task 6: 입력 시스템 IsChatFocused 가드 삽입

대상 파일 및 차단 키:

| 파일 (실제 경로) | 차단 키 | 삽입 위치 |
|---|---|---|
| `Systems/Commands/UnitControl/UnitCommandInputSystem.cs` | A (AttackMove) | OnUpdate 키보드 입력 처리 시작부 |
| `Systems/Commands/StructureAction/StructureCommandInputSystem.cs` | Q, W, E, R | OnUpdate 키보드 입력 처리 시작부 |
| `Systems/Commands/Construction/ConstructionMenuInputSystem.cs` | Q, W, E, R | OnUpdate 키보드 입력 처리 시작부 |
| `Controller/Camera/CameraSystem.cs` | T (키보드만, 마우스 유지) | T키 처리 분기에만 |
| `Systems/Commands/Selection/EntitySelectionSystem.cs` | ESC | ESC 처리 분기에만 |

> StructurePlacementInputSystem.cs는 ESC 키를 처리하지 않으므로 가드 삽입 대상에서 제외.

삽입 패턴:
```csharp
if (ChatUIController.IsChatFocused) return;  // 키보드 입력 전체 차단
```
또는 개별 키 분기 (Unity Input System):
```csharp
if (!ChatUIController.IsChatFocused && Keyboard.current.tKey.wasPressedThisFrame) { ... }
```

주의: 마우스 입력(엣지 패닝, 스크롤 줌, 유닛 선택 클릭)은 채팅 중에도 유지해야 함.

## 병렬 작업 구성 (subagent 활용)

| Agent | 작업 내용 | 의존성 |
|---|---|---|
| Agent A | Task 1~4: ChatUIController 본체 | 없음 |
| Agent B | Task 6: 입력 시스템 가드 삽입 (5개 파일) | 없음 (IsChatFocused 시그니처만 알면 됨) |
| Agent C | Task 5: 이벤트 바인딩 | Agent A 완료 후 |

## 테스트 요구사항

### EditMode Test
- 링버퍼 50개 초과 시 oldest 제거 확인
- 메시지 길이 UTF-8 200 bytes 경계값 테스트 (한글 66자 ≈ 198 bytes)

### 수동 검증
- Enter 토글: 비활성 → 활성 → 입력 → Enter → 전송 + 비활성
- ESC: 포커스 해제, 입력 내용 유지
- 채널 탭 전환: 로비/인게임 상태별 탭 표시
- A/Q/W/E/R/T/ESC 키가 채팅 포커스 중 게임 명령 발생시키지 않음
- 마우스 입력이 채팅 중에도 정상 동작

## 검증 방법
- 채팅 UI 표시 + 메시지 송수신 시각적 확인
- 입력 시스템 격리: 채팅 포커스 중 키보드 명령 무반응
- 채널 전환: 로비↔인게임 전환 시 탭 자동 변경
- 에러 피드백: Rate Limit 초과 시 시스템 메시지 표시

## 완료 기준
- [ ] ChatUIController.cs 전체 구현
- [ ] 메시지 링버퍼 50개 정상 동작
- [ ] 채널 탭 로비/인게임 자동 전환
- [ ] IsChatFocused 가드 5개 파일 삽입 완료
- [ ] ChatClient 이벤트 바인딩 동작 확인
- [ ] EditMode Test 통과
- [ ] 마우스 입력 채팅 중 정상 동작 확인
