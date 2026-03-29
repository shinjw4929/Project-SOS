# 룸 서버 연동 오케스트레이션 플랜

> **전제**: C++ 룸 서버(Project-SOS-Backend)가 완성된 상태. Unity 프로젝트에 룸 서버 클라이언트를 구현하여 연동한다.
> **레퍼런스**: [기존 상세 설계](./룸%20서버%20연동%20계획.md), [room.proto](../../../Project-SOS-Backend/proto/room.proto)

---

## 문제 정의

- 현재 Unity 클라이언트는 `GameBootStrap.cs`에서 `AutoConnectPort = 7979`로 게임 서버에 직접 자동 접속한다.
- 인증 없이 누구나 게임 서버에 접속 가능하며, 방 생성/참가/매칭 과정이 없다.
- 멀티플레이어 게임으로서 로비 → 방 대기 → 게임 시작의 흐름이 필요하나 구현되어 있지 않다.
- 백엔드(Room Server :8080, Internal :8081)는 완성되었으나 Unity 측 연동 코드가 전혀 없다.

## 영향 범위

- **Client**: RoomClient(TCP), RoomUIController, GoInGameClientSystem, NetcodeConnectionUtil, RoomAuthState
- **Server**: TokenValidationSystem, SlotNotifySystem, GoInGameServerSystem, RoomSessionInfo, TokenValidatedTag
- **Shared**: GoInGameRequestRpc
- **Bootstrap**: GameBootStrap.cs (`Assets/Scripts/GameBootStrap.cs`, AutoConnectPort 변경)

---

## AS-IS (현재 상태)

### 접속 흐름
```
앱 시작 → GameBootStrap (AutoConnectPort=7979) → Netcode 자동 접속
→ GoInGameClientSystem: NetworkId 감지 → 즉시 GoInGameRequestRpc 전송
→ GoInGameServerSystem: RPC 수신 → Hero 생성 + NetworkStreamInGame 부착
```

### 관련 파일
| 파일 | 현재 역할 |
|------|----------|
| `Assets/Scripts/GameBootStrap.cs:18` | `AutoConnectPort = 7979` (자동 접속) |
| `GoInGameClientSystem.cs` | NetworkId 존재 시 즉시 GoInGameRequestRpc 전송 |
| `GoInGameServerSystem.cs` | 모든 GoInGameRequestRpc 무조건 처리, Hero 생성 |
| `GoInGameRequestRpc.cs` | 빈 구조체 (`IRpcCommand`, 필드 없음) |

### 현재 동작 방식
- 인증/토큰 없음 — 누구든 :7979에 접속하면 게임 진입
- 방/로비 개념 없음 — 접속 즉시 게임 시작
- 세션 관리 없음 — 연결 끊김 시 룸 서버에 통지하지 않음

---

## TO-BE (목표 상태)

### 접속 흐름
```
앱 시작 → RoomClient가 룸 서버(:8080) TCP 접속
→ 로비: 방 목록 조회 / 방 생성 / 방 참가
→ 대기실: 준비 토글, 호스트 게임 시작
→ GameStart 수신: auth_token + session_id + game_server_host:port
→ NetcodeConnectionUtil로 게임 서버(:7979) 수동 접속
→ GoInGameClientSystem: RoomAuthState에서 토큰 읽어 GoInGameRequestRpc(AuthToken) 전송
→ TokenValidationSystem: 룸 서버 :8081로 토큰 검증 → TokenValidatedTag 부착
→ GoInGameServerSystem: TokenValidatedTag 있는 RPC만 처리 → Hero 생성
→ SlotNotifySystem: 연결 끊김 시 SlotReleased 전송 + 주기적 Heartbeat
```

### 변경/추가/삭제 항목
| 구분 | 항목 |
|------|------|
| **신규** | RoomClient.cs, NetcodeConnectionUtil.cs, RoomAuthState.cs, RoomUIController.cs |
| **신규** | TokenValidationSystem.cs, RoomTokenValidator.cs, SlotNotifySystem.cs, SlotNotifyClient.cs |
| **신규** | RoomSessionInfo.cs (Server), TokenValidatedTag.cs (Server) |
| **신규** | Shared/Network/Generated/ (protoc C# 코드, Client+Server 모두 참조) |
| **신규** | Shared/Network/ProtobufFraming.cs (4byte LE 프레이밍) |
| **수정** | GameBootStrap.cs (AutoConnectPort = 0) |
| **신규** | Assets/Plugins/Protobuf/ (Google.Protobuf DLL, 전체 asmdef auto-reference) |
| **수정** | GoInGameRequestRpc.cs (AuthToken 필드 추가) |
| **수정** | GoInGameClientSystem.cs (토큰 포함 전송) |
| **수정** | GoInGameServerSystem.cs (WithAll<TokenValidatedTag> 추가) |

---

## AS-IS vs TO-BE 비교표

| 항목 | AS-IS | TO-BE |
|------|-------|-------|
| 게임 서버 접속 | AutoConnectPort=7979 자동 접속 | 룸 서버 GameStart 수신 후 수동 접속 |
| 인증 | 없음 | auth_token 기반 토큰 검증 |
| GoInGameRequestRpc | 빈 구조체 | AuthToken 필드 포함 |
| GoInGameServerSystem | 모든 RPC 무조건 처리 | TokenValidatedTag 필터링 |
| 로비/방 관리 | 없음 | RoomClient + RoomUIController |
| 세션 추적 | 없음 | RoomSessionInfo + SlotNotifySystem |
| 연결 끊김 통지 | 없음 | SlotReleased + GameServerHeartbeat |
| 프로토콜 | 없음 | Protobuf (room.proto) + 4byte LE length prefix |

---

## Phase 체크리스트

### Phase 1: Protobuf 환경 구성 + 연결 인프라 전환
- [x] Google.Protobuf DLL을 Assets/Plugins/Protobuf/에 배치 (전체 asmdef auto-reference)
- [x] room.proto에서 C# 코드 생성 → Shared/Network/Generated/에 배치
- [x] Client/Server/Shared asmdef 모두에서 Protobuf 타입 접근 확인
- [x] 프레이밍 유틸리티 구현 (Shared/Network/ProtobufFraming.cs)
- [x] GameBootStrap.cs: AutoConnectPort = 0
- [x] NetcodeConnectionUtil.cs: 수동 Netcode 연결 유틸리티
- [x] 컴파일 확인

-> 상세: [phase-1-protobuf-infra.md](./phase-1-protobuf-infra.md)

### Phase 2: 룸 클라이언트 구현
- [x] RoomClient.cs: TCP 비동기 통신 (async/await + TcpClient)
- [x] Envelope 송수신 (Protobuf 직렬화/역직렬화)
- [x] 상태 머신 (Disconnected → Lobby → InRoom → Matched → InGame)
- [x] Heartbeat (10초 간격)
- [x] RoomAuthState.cs: 토큰 전달용 ECS 싱글톤
- [x] ClientBootstrapSystem에 RoomAuthState 초기화 추가
- [x] RoomClient → ECS 브리징 (World.DefaultGameObjectInjectionWorld 경유)
- [x] DontDestroyOnLoad 생명주기 관리

-> 상세: [phase-2-room-client.md](./phase-2-room-client.md)

### Phase 3: 토큰 검증 + 슬롯 관리
- [x] GoInGameRequestRpc.cs: AuthToken 필드 추가
- [x] GoInGameClientSystem.cs: RoomAuthState에서 토큰 읽어 전송
- [x] TokenValidatedTag.cs, RoomSessionInfo.cs 컴포넌트 생성
- [x] RoomTokenValidator.cs: 룸 서버 :8081 TCP 토큰 검증
- [x] TokenValidationSystem.cs: managed SystemBase, 토큰 검증 → Tag 부착
- [x] GoInGameServerSystem.cs: WithAll<TokenValidatedTag> 추가
- [x] SlotNotifyClient.cs: SlotReleased + GameServerHeartbeat 전송
- [x] SlotNotifySystem.cs: 연결 끊김 감지 + 슬롯 해제 통지

-> 상세: [phase-3-token-slot.md](./phase-3-token-slot.md)

### Phase 4: 룸 UI
- [x] RoomUIController.cs: 방 목록 / 방 생성 / 대기실 화면
- [x] RoomClient 이벤트 바인딩 (콜백 → UI 갱신)
- [x] 상태별 화면 전환 (Connecting/Lobby/InRoom/GameStarting/InGame/Error)

-> 상세: [phase-4-room-ui.md](./phase-4-room-ui.md)

### Phase 5: 에러 처리 + 통합 테스트
- [x] 룸 서버 접속 실패 재시도 로직
- [x] 대기 중 연결 끊김 처리
- [x] GameStart 후 게임 서버 접속 실패 처리
- [x] 토큰 만료(60초) 처리
- [x] 게임 종료 후 재플레이 (ReturnToLobby)
- [x] 통합 테스트 시나리오 검증

-> 상세: [phase-5-error-test.md](./phase-5-error-test.md)

---

## Phase 간 의존성

| Phase | 의존성 | 병렬 가능 |
|-------|--------|----------|
| 1 | 없음 | - |
| 2 | Phase 1 (Protobuf 코드젠 필요) | X |
| 3 | Phase 2 (RoomAuthState, RoomClient 필요) | X |
| 4 | Phase 2 (RoomClient 이벤트 필요) | O (Phase 3과 병렬) |
| 5 | Phase 3 + 4 완료 | X |

---

## 변경 파일 요약

| Phase | 파일 | 변경 |
|-------|------|------|
| 1 | `GameBootStrap.cs` | AutoConnectPort = 0 |
| 1 | `Shared/Network/Generated/*.cs` | protoc 생성 코드 (신규, Shared — Client+Server 모두 참조) |
| 1 | `Shared/Network/ProtobufFraming.cs` | 4byte LE 프레이밍 (신규, Shared — Client+Server 모두 참조) |
| 1 | `Assets/Plugins/Protobuf/` | Google.Protobuf DLL 배치 (전체 asmdef auto-reference) |
| 1 | `Client/Network/NetcodeConnectionUtil.cs` | 수동 연결 유틸리티 (신규) |
| 2 | `Client/Network/RoomClient.cs` | TCP 클라이언트 (신규) |
| 2 | `Client/Component/Singleton/RoomAuthState.cs` | 토큰 싱글톤 (신규) |
| 2 | `Client/Systems/Initialize/ClientBootstrapSystem.cs` | RoomAuthState 초기화 추가 |
| 3 | `Shared/RPCs/GoInGameRequestRpc.cs` | AuthToken 필드 추가 |
| 3 | `Client/Systems/Initialize/GoInGameClientSystem.cs` | 토큰 포함 전송 |
| 3 | `Server/Data/RoomSessionInfo.cs` | 세션 정보 컴포넌트 (신규, Server 전용) |
| 3 | `Server/Data/TokenValidatedTag.cs` | 검증 태그 (신규, Server 전용) |
| 3 | `Server/Network/RoomTokenValidator.cs` | 토큰 검증 TCP (신규) |
| 3 | `Server/Network/SlotNotifyClient.cs` | 슬롯 해제 TCP (신규) |
| 3 | `Server/Systems/TokenValidationSystem.cs` | 토큰 검증 시스템 (신규) |
| 3 | `Server/Systems/SlotNotifySystem.cs` | 슬롯 관리 시스템 (신규) |
| 3 | `Server/GoInGameServerSystem.cs` | WithAll<TokenValidatedTag> 추가 |
| 4 | `Client/Controller/UI/RoomUIController.cs` | 룸 UI (신규) |
| 4 | `Prefabs/UI/RoomScreen.prefab` | UI 프리팹 (신규) |
| 5 | 다수 | 에러 처리 + 엣지 케이스 보강 |
| 5 | `Docs/Systems/코드베이스 구조.md` | 신규 폴더/파일 구조 반영 |
| 5 | `Docs/Systems/시스템 그룹 및 의존성.md` | TokenValidationSystem, SlotNotifySystem 추가 |
| 5 | `Docs/Architecture.md` | 접속 흐름 변경, 토큰 검증 패턴 추가 |

---

## 검증 방법

1. **Phase 1**: protoc 생성 코드 컴파일 성공, 수동 연결 API 호출 가능
2. **Phase 2**: RoomClient → 룸 서버 TCP 접속, 방 생성/참가/퇴장 메시지 송수신 확인
3. **Phase 3**: 토큰 없이 :7979 직접 접속 시 Disconnect, 유효 토큰으로 정상 진입 확인
4. **Phase 4**: UI에서 방 목록/생성/대기실 화면 전환 및 이벤트 동작 확인
5. **Phase 5**: 에러 시나리오(접속 실패, 토큰 만료, 연결 끊김) 정상 처리 확인

---

## 롤백 전략

- **Phase 1 롤백**: Generated 폴더 삭제, GameBootStrap.cs의 AutoConnectPort 복원
- **Phase 2 롤백**: RoomClient.cs, RoomAuthState.cs 삭제
- **Phase 3 롤백**: GoInGameRequestRpc AuthToken 제거, TokenValidation/SlotNotify 시스템 삭제, GoInGameServerSystem 쿼리 복원
- **Phase 4 롤백**: RoomUIController.cs + RoomScreen.prefab 삭제
- **Phase 5 롤백**: 에러 처리 코드 제거 (각 파일 부분 revert)
