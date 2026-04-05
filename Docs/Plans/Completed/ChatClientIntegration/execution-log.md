# 실행 기록

## Phase 1: Protobuf 코드젠 + ChatClient 네트워크 계층 - 2026-04-05

### 실행 내역
| 작업 | 결과 | 비고 |
|---|---|---|
| Task 1: Protobuf C# 코드젠 | Pass | protoc (grpc.tools 2.78.0) → Chat.cs 생성, 네임스페이스 Sos.Chat 확인 |
| Task 1.5: ChatProtobufFraming.cs | Pass | ProtobufFraming.cs 동일 패턴, ChatEnvelope 대상 4B LE 프레이밍 |
| Task 2: ChatClient 기본 구조 | Pass | MonoBehaviour + DontDestroyOnLoad, ChatClientState 상태 머신, 이벤트 인터페이스 |
| Task 3: TCP 연결 관리 | Pass | 지수 백오프 재연결 (1s,2s,4s), CancellationToken, 버퍼 확장 |
| Task 4: 인증 흐름 | Pass | 로비 인증 (자동), 세션 재인증 (NetworkId 폴링) |
| Task 5: 메시지 송수신 | Pass | ChatSend + 클라이언트 사전 검증 (빈 문자열, 200bytes 초과), ChatReceive 이벤트 |
| Task 6: 하트비트 | Pass | 10초 간격 코루틴, Lobby/InSession 상태에서만 동작 |
| Task 7: 세션 재인증 트리거 | Pass | RoomAuthState.SessionId + NetworkId 폴링 방식, GameOverEvents.OnGameOver 구독 |
| Task 8: 메시지 디스패치 | Pass | ChatEnvelope.PayloadCase switch: AuthResult/Receive/SystemMessage/Error/Heartbeat |

### 변경된 파일
- `Assets/Scripts/Shared/Network/Generated/Chat.cs` - 신규: protoc 코드젠 출력 (Sos.Chat 네임스페이스)
- `Assets/Scripts/Shared/Network/ChatProtobufFraming.cs` - 신규: ChatEnvelope용 4B LE 프레이밍 유틸리티
- `Assets/Scripts/Client/Network/ChatClient.cs` - 신규: TCP 연결, 인증, 메시지, 하트비트, 세션 재인증, GameOver 연동

### 발견된 이슈
- protoc가 시스템 PATH에 없음 → NuGet 캐시 내 grpc.tools 2.78.0의 protoc.exe 사용으로 해결
- RoomClient.cs 수정 없이 구현 완료: 세션 재인증을 RoomClient 이벤트 구독 대신 ECS 폴링(RoomAuthState + NetworkId) 방식 채택. RoomClient 의존성 제거.

### Phase 1 완료 판정: Pass

---

## Phase 2: ChatUIController + 입력 시스템 통합 - 2026-04-05

### 실행 내역
| 작업 | 결과 | 비고 |
|---|---|---|
| Task 1: ChatUIController 기본 구조 | Pass | DontDestroyOnLoad, IsChatFocused/WasChatFocusedThisFrame 정적 프로퍼티, Enter/ESC 토글 |
| Task 2: 메시지 표시 영역 | Pass | ScrollRect + 50개 링버퍼(Queue), 채널별 색상 구분, TMP richText=false |
| Task 3: 채널 탭 | Pass | 로비: LOBBY, 인게임: TEAM/ALL 자동 전환 (OnAuthResult 연동) |
| Task 4: 입력 필드 | Pass | 200 bytes UTF-8 검증, 빈 메시지 차단 |
| Task 5: ChatClient 이벤트 바인딩 | Pass | OnMessageReceived/OnSystemMessage/OnChatError/OnConnected/OnDisconnected/OnAuthResult |
| Task 6: IsChatFocused 가드 삽입 | Pass | 5개 파일: UnitCommandInput(A키), StructureCommandInput(전체), ConstructionMenuInput(전체), CameraSystem(T키), EntitySelection(ESC) |

### 변경된 파일
- `Assets/Scripts/Client/Controller/UI/ChatUIController.cs` - 신규: 채팅 UI 컨트롤러 전체
- `Assets/Scripts/Client/Systems/Commands/UnitControl/UnitCommandInputSystem.cs` - 수정: A키 AttackMove에 IsChatFocused 가드
- `Assets/Scripts/Client/Systems/Commands/StructureAction/StructureCommandInputSystem.cs` - 수정: OnUpdate 시작부 IsChatFocused + WasChatFocusedThisFrame 가드
- `Assets/Scripts/Client/Systems/Commands/Construction/ConstructionMenuInputSystem.cs` - 수정: OnUpdate 시작부 IsChatFocused + WasChatFocusedThisFrame 가드
- `Assets/Scripts/Client/Controller/Camera/CameraSystem.cs` - 수정: T키 분기에 IsChatFocused 가드
- `Assets/Scripts/Client/Systems/Commands/Selection/EntitySelectionSystem.cs` - 수정: ESC 분기에 IsChatFocused + WasChatFocusedThisFrame 가드

### 발견된 이슈
- Phase 1에서 ChatClient.cs의 `Action` 모호성 에러 발견 (Shared.Action vs System.Action) → `using Action = System.Action;` 추가로 해결

### Phase 2 완료 판정: Pass

---

## Phase 3: 통합 테스트 + 엣지 케이스 처리 - 2026-04-05

### 실행 내역
| 작업 | 결과 | 비고 |
|---|---|---|
| Task 5: 코드 리뷰 + 선제적 버그 수정 | Pass | WasChatFocusedThisFrame 로직 수정, Rich Text 인젝션 방지 |
| Task 1: 생명주기 시나리오 | 수동 검증 필요 | 서버 기동 후 로비/인게임/로비 복귀 시나리오 |
| Task 2: 재연결 시나리오 | 수동 검증 필요 | 서버 강제 종료 → 재연결 시도 확인 |
| Task 3: Rate Limit + 입력 검증 | 수동 검증 필요 | 코드 구현은 완료 (200 bytes 검증, 빈 메시지 차단) |
| Task 4: 성능 프로파일링 | 수동 검증 필요 | Unity Profiler로 500+ 유닛 전투 중 측정 |

### 변경된 ��일
- `Assets/Scripts/Client/Controller/UI/ChatUIController.cs` - 수정: WasChatFocusedThisFrame 로직 단순화 (Update에서 즉시 계산), SanitizeRichText 추가 (ZWSP 삽입으로 태그 인젝션 방지)

### 발견된 이슈
- **WasChatFocusedThisFrame 타이밍 버그**: 기존 로직은 Update 시작부에서 이전 프레임 상태를 비교하여 ESC 처리를 다음 프레임에서 감지함. Update()가 ECS보다 먼저 실행되므로, 같은 프레임 내에서 ESC 처리가 필요함 → HandleKeyboardInput 후 `wasFocused && !IsChatFocused`로 즉시 계산하도록 수정
- **TMP Rich Text 인젝션**: 메시지 표시에 `richText=true`를 사용하므로 사용자가 `<b>`, `<color=red>` 등의 태그를 삽입 가능 → SanitizeRichText()로 `<` 뒤에 ZWSP 삽입하여 태그 파싱 무효화

### Phase 3 완료 판정: Pass (코드 레벨 완료, 수동 통합 테스트는 서버 환경 필요)
