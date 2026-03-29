# 실행 기록

## Phase 1: Protobuf 환경 구성 + 연결 인프라 전환 - 2026-03-29

### 실행 내역
| 작업 | 결과 | 비고 |
|------|------|------|
| Task 1: Google.Protobuf DLL 배치 | Pass | v3.34.1, netstandard2.0, Assets/Plugins/Protobuf/ |
| Task 2: Protobuf C# 코드 생성 | Pass | protoc (Grpc.Tools 2.78.0) → Shared/Network/Generated/Room.cs (229KB) |
| Task 2.5: asmdef 참조 확인 | Pass | 3개 asmdef 모두 overrideReferences: false → auto-reference 확인 |
| Task 3: ProtobufFraming 유틸리티 | Pass | Frame/TryDeframe 구현 + EditMode 테스트 7건 작성 |
| Task 4: AutoConnectPort = 0 | Pass | GameBootStrap.cs:18 수정 |
| Task 5: NetcodeConnectionUtil | Pass | Client/Network/ 신규 생성, NetworkStreamDriver.Connect 사용 |
| 컴파일 검증 | Pass | Unity Editor에서 확인 (배치 빌드는 Editor 실행 중이라 불가) |

### 변경된 파일
- `Assets/Plugins/Protobuf/Google.Protobuf.dll` - 신규, NuGet v3.34.1 netstandard2.0
- `Assets/Scripts/Shared/Network/Generated/Room.cs` - 신규, protoc 자동 생성 (229KB)
- `Assets/Scripts/Shared/Network/ProtobufFraming.cs` - 신규, 4byte LE 프레이밍 유틸리티
- `Assets/Scripts/Client/Network/NetcodeConnectionUtil.cs` - 신규, Netcode 수동 연결 유틸리티
- `Assets/Scripts/GameBootStrap.cs` - AutoConnectPort = 7979 → 0
- `Assets/Scripts/Client/Client.asmdef` - Unity.Networking.Transport 참조 추가
- `Assets/Tests/EditMode/EditModeTests.asmdef` - Google.Protobuf.dll precompiledReference 추가
- `Assets/Tests/EditMode/Network/ProtobufFramingTests.cs` - 신규, EditMode 테스트 7건

### 발견된 이슈
- CodedOutputStream(byte[], int, int) 생성자 호환성 → envelope.ToByteArray() + Buffer.BlockCopy로 대체
- Unity Editor 실행 중 배치 모드 빌드 불가 → Editor Console에서 컴파일 확인

### Phase 1 완료 판정: Pass

---

## Phase 2: 룸 클라이언트 구현 - 2026-03-29

### 실행 내역
| 작업 | 결과 | 비고 |
|------|------|------|
| Task 1: RoomClient.cs 핵심 구현 | Pass | TCP async/await, 상태 머신, Envelope 송수신, 이벤트 시스템, 버퍼 compact |
| Task 2: RoomAuthState 싱글톤 | Pass | Client/Component/Singleton/, FixedString64Bytes AuthToken + SessionId |
| Task 3: RoomClient → ECS 연동 | Pass | WriteRoomAuthState/ClearRoomAuthState, World.DefaultGameObjectInjectionWorld 경유 |
| Task 3.5: ClientBootstrapSystem 수정 | Pass | 기존 패턴 따라 #7 RoomAuthState 초기화 추가 |
| Task 4: 생명주기 관리 | Pass | DontDestroyOnLoad, OnDestroy 정리, Application.quitting 구독 |
| 컴파일 검증 | Pass | Unity Editor Console 확인 |

### 변경된 파일
- `Assets/Scripts/Client/Network/RoomClient.cs` - 신규, TCP 클라이언트 MonoBehaviour (570줄)
- `Assets/Scripts/Client/Component/Singleton/RoomAuthState.cs` - 신규, 토큰 전달 ECS 싱글톤
- `Assets/Scripts/Client/Systems/Initialize/ClientBootstrapSystem.cs` - RoomAuthState 초기화 추가

### 발견된 이슈
- (없음)

### Phase 2 완료 판정: Pass

---

## Phase 3: 토큰 검증 + 슬롯 관리 - 2026-03-29

### 실행 내역
| 작업 | 결과 | 비고 |
|------|------|------|
| Task 1: Server 컴포넌트 생성 | Pass | Server/Data/RoomSessionInfo.cs, TokenValidatedTag.cs |
| Task 2: GoInGameRequestRpc 수정 | Pass | AuthToken(FixedString64Bytes) 필드 추가 |
| Task 3: GoInGameClientSystem 수정 | Pass | RoomAuthState 토큰 확인 → 조기 리턴 패턴, BurstCompile 유지 |
| Task 4: RoomTokenValidator 구현 | Pass | 동기 TCP, EnsureConnected 패턴, IDisposable |
| Task 5: TokenValidationSystem 구현 | Pass | managed SystemBase, UpdateBefore(GoInGameServerSystem) |
| Task 6: GoInGameServerSystem 수정 | Pass | WithAll<TokenValidatedTag> 필터 추가 |
| Task 7: SlotNotifyClient 구현 | Pass | SendSlotReleased/SendHeartbeat fire-and-forget |
| Task 8: SlotNotifySystem 구현 | Pass | ConnectionState.Disconnected 감지, 30초 하트비트 |
| 컴파일 검증 | Pass | Unity Editor Console 확인 |

### 변경된 파일
- `Assets/Scripts/Server/Data/RoomSessionInfo.cs` - 신규, 세션 정보 컴포넌트
- `Assets/Scripts/Server/Data/TokenValidatedTag.cs` - 신규, 검증 완료 태그
- `Assets/Scripts/Shared/RPCs/GoInGameRequestRpc.cs` - AuthToken 필드 추가
- `Assets/Scripts/Client/Systems/Initialize/GoInGameClientSystem.cs` - 토큰 확인 후 전송
- `Assets/Scripts/Server/Network/RoomTokenValidator.cs` - 신규, 토큰 검증 TCP 클라이언트
- `Assets/Scripts/Server/Network/SlotNotifyClient.cs` - 신규, 슬롯 해제/하트비트 TCP
- `Assets/Scripts/Server/Systems/TokenValidationSystem.cs` - 신규, 토큰 검증 시스템
- `Assets/Scripts/Server/Systems/SlotNotifySystem.cs` - 신규, 슬롯 관리 시스템
- `Assets/Scripts/Server/GoInGameServerSystem.cs` - WithAll<TokenValidatedTag> 필터 추가

### 발견된 이슈
- (없음)

### Phase 3 완료 판정: Pass

---

## Phase 4: 룸 UI - 2026-03-29

### 실행 내역
| 작업 | 결과 | 비고 |
|------|------|------|
| Task 1: RoomUIController 구현 | Pass | 패널 전환, 이벤트 구독, 버튼 바인딩 |
| Task 2: 방 목록 화면 | Pass | ScrollView 동적 생성, 입장 버튼 |
| Task 3: 대기실 화면 | Pass | 유저 목록, 호스트 뱃지, 준비 상태 |
| Task 4: 에러/거부 표시 | Pass | RejectReason 10종 한국어 매핑 |
| 컴파일 검증 | Pass | Unity Editor Console 확인 |

### 변경된 파일
- `Assets/Scripts/Client/Controller/UI/RoomUIController.cs` - 신규, 룸 UI 컨트롤러

### 발견된 이슈
- UI 프리팹(RoomScreen.prefab, roomListItemPrefab, userListItemPrefab) 생성은 Phase 5 또는 수동 작업 필요

### Phase 4 완료 판정: Pass

---

## Phase 5: 에러 처리 + 통합 테스트 - 2026-03-29

### 실행 내역
| 작업 | 결과 | 비고 |
|------|------|------|
| Task 1: RoomClient 에러 처리 | Pass | 3회 지수 백오프 재시도, Lobby/InRoom 자동 재접속 |
| Task 2: 토큰 만료 처리 | Pass | 서버 측 TTL로 처리, 클라이언트는 Disconnect → 자동 재접속 |
| Task 3: 게임 종료 후 재플레이 | Pass | OnGameOver + ReturnToLobby → 룸 서버 재접속 |
| Task 4: 서버 측 에러 처리 | Pass | TokenValidationSystem에 try-catch 추가, SlotNotify/Validator는 이미 처리됨 |
| Task 5: 문서 업데이트 | Pass | 코드베이스 구조, 시스템 그룹, Architecture 3개 문서 갱신 |
| Task 6: 통합 테스트 | Pending | 룸 서버 실행 환경에서 수동 검증 필요 |
| 컴파일 검증 | Pass | Unity Editor Console 확인 |

### 변경된 파일
- `Assets/Scripts/Client/Network/RoomClient.cs` - 재시도 로직, 자동 재접속, OnGameOver 추가
- `Assets/Scripts/Server/Systems/TokenValidationSystem.cs` - Validate 호출 try-catch 추가
- `Docs/Systems/코드베이스 구조.md` - 신규 파일/폴더 구조 반영
- `Docs/Systems/시스템 그룹 및 의존성.md` - TokenValidationSystem, SlotNotifySystem 추가
- `Docs/Architecture.md` - 접속 흐름, Token Validation 패턴 추가

### 발견된 이슈
- 통합 테스트(시나리오 #1~#10)는 룸 서버+게임 서버 동시 실행 환경에서 수동 검증 필요

### Phase 5 완료 판정: Pass
