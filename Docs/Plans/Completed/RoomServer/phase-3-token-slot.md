# Phase 3: 토큰 검증 + 슬롯 관리

## 목표

게임 서버가 룸 서버를 거치지 않은 직접 접속을 거부하도록 토큰 검증을 구현하고, 연결 끊김 시 룸 서버에 슬롯 해제를 통지한다.

## 선행 조건

- Phase 2 완료 (RoomClient, RoomAuthState, NetcodeConnectionUtil)
- 룸 서버 내부 채널(:8081)이 TokenValidateRequest/Response를 처리하는 상태

---

## 작업 목록

### Task 1: Server 컴포넌트 생성

**파일 (신규)**: `Assets/Scripts/Server/Data/RoomSessionInfo.cs`

- [ ] IComponentData 구현 (Server 전용 — TokenValidationSystem이 생성, SlotNotifySystem이 소비)
  - 기존 서버 전용 IComponentData(`PendingBuildServerData` 등)와 동일 디렉토리
  ```csharp
  public struct RoomSessionInfo : IComponentData
  {
      public FixedString64Bytes SessionId;
      public FixedString64Bytes UserId;  // Proto의 player_id 대응, Unity 측은 "User" 네이밍
  }
  ```

**파일 (신규)**: `Assets/Scripts/Server/Data/TokenValidatedTag.cs`

- [ ] 빈 IComponentData (태그 컴포넌트, Server 전용)
  ```csharp
  public struct TokenValidatedTag : IComponentData { }
  ```

### Task 2: GoInGameRequestRpc 수정

**파일**: `Assets/Scripts/Shared/RPCs/GoInGameRequestRpc.cs`

- [ ] AuthToken 필드 추가:
  ```csharp
  public struct GoInGameRequestRpc : IRpcCommand
  {
      public FixedString64Bytes AuthToken;
  }
  ```

### Task 3: GoInGameClientSystem 수정

**파일**: `Assets/Scripts/Client/Systems/Initialize/GoInGameClientSystem.cs`

- [ ] RoomAuthState 싱글톤에서 토큰 읽기
- [ ] 토큰이 없으면 (AuthToken.Length == 0) RPC 전송 안 함
- [ ] 토큰이 있으면 GoInGameRequestRpc.AuthToken에 포함하여 전송
- [ ] Burst 호환성 유지 (FixedString64Bytes는 blittable)

```csharp
// 변경 핵심 로직
if (!SystemAPI.TryGetSingleton<RoomAuthState>(out var authState) || authState.AuthToken.Length == 0)
    return;  // 토큰 없으면 대기

// 기존 로직 + AuthToken 포함
entityCommandBuffer.AddComponent(rpcEntity, new GoInGameRequestRpc
{
    AuthToken = authState.AuthToken
});
```

### Task 4: RoomTokenValidator 구현

**파일 (신규)**: `Assets/Scripts/Server/Network/RoomTokenValidator.cs`

- [ ] managed 클래스 (Burst 미적용)
- [ ] 룸 서버 :8081에 전용 TCP 연결 1개 유지
- [ ] `TokenValidateResult Validate(string token)` 메서드:
  1. Envelope(TokenValidateRequest(token)) 전송
  2. Envelope(TokenValidateResponse) 수신
  3. 결과 반환: { Valid, UserId, SessionId }
- [ ] 동기 TCP 조회 (localhost < 1ms)
- [ ] IDisposable 구현 (TCP 정리)
- [ ] 연결 끊김 시 재연결 시도

### Task 5: TokenValidationSystem 구현

**파일 (신규)**: `Assets/Scripts/Server/Systems/TokenValidationSystem.cs`

- [ ] managed SystemBase (Burst 미적용, TCP 통신 필요)
- [ ] `[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]`
- [ ] `[UpdateBefore(typeof(GoInGameServerSystem))]`
- [ ] OnCreate: RoomTokenValidator 생성 (localhost:8081)
- [ ] OnUpdate:
  ```
  Query: GoInGameRequestRpc + ReceiveRpcCommandRequest + WithNone<TokenValidatedTag>
  각 RPC에서:
    1. AuthToken 추출
    2. RoomTokenValidator.Validate(token) 호출
    3. 유효 → RPC 엔티티에 TokenValidatedTag 부착
           → SourceConnection에 RoomSessionInfo(UserId, SessionId) 부착
    4. 무효 → SourceConnection에 NetworkStreamRequestDisconnect 부착
           → RPC 엔티티 파괴
  ```
- [ ] OnDestroy: RoomTokenValidator.Dispose()

### Task 6: GoInGameServerSystem 수정

**파일**: `Assets/Scripts/Server/GoInGameServerSystem.cs`

- [ ] 기존 쿼리에 `.WithAll<TokenValidatedTag>()` 추가
- [ ] 나머지 로직 변경 없음 (BurstCompile 유지 가능)

### Task 7: SlotNotifyClient 구현

**파일 (신규)**: `Assets/Scripts/Server/Network/SlotNotifyClient.cs`

- [ ] managed 클래스 (Burst 미적용)
- [ ] 룸 서버 :8081에 별도 TCP 연결 1개 (RoomTokenValidator와 독립)
  - 이유: TokenValidateRequest는 요청-응답, SlotReleased는 일방향. 분리하면 수신 매칭 불필요.
- [ ] `void SendSlotReleased(string userId, string sessionId)` — 일방향 전송 (Proto의 player_id 필드에 매핑)
- [ ] `void SendHeartbeat(string serverId, uint activeSessions)` — 일방향 전송
  - serverId: 환경 변수 또는 GameSettings에서 구성 (배포 환경별 고유 식별자)
- [ ] IDisposable 구현
- [ ] 전송 실패 시 로그 경고 후 무시 (룸 서버 측 TTL로 자동 복구)

### Task 8: SlotNotifySystem 구현

**파일 (신규)**: `Assets/Scripts/Server/Systems/SlotNotifySystem.cs`

- [ ] managed SystemBase (Burst 미적용, TCP 필요)
- [ ] `[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]`
- [ ] OnCreate: SlotNotifyClient 생성 (localhost:8081)
- [ ] OnUpdate:
  ```
  1. 연결 끊김 감지:
     RoomSessionInfo가 있고 ConnectionState.State == Disconnected인 엔티티
     (또는 NetworkStreamRequestDisconnect 존재 여부로 감지)
     → SessionId + UserId 읽기
     → SlotNotifyClient.SendSlotReleased()
     → RoomSessionInfo 컴포넌트 제거 (중복 방지)

  2. 30초마다 Heartbeat:
     → SlotNotifyClient.SendHeartbeat(serverId, activePlayerCount)
  ```
- [ ] OnDestroy: SlotNotifyClient.Dispose()

---

## 병렬 작업 구성 (subagent 활용)

| Agent | 작업 내용 | 의존성 |
|-------|----------|--------|
| Agent A | Task 1 + 2 + 3 (Server 컴포넌트 + RPC + 클라이언트 수정) | 없음 |
| Agent B | Task 4 + 5 + 6 (토큰 검증 서버 측) | 없음 |
| Agent C | Task 7 + 8 (슬롯 관리) | 없음 |
| Main | 통합 검증 | Agent A, B, C 완료 후 |

---

## TCP 연결 요약

```
게임 서버(:7979) → 룸 서버(:8081)

연결 1 (TokenValidationSystem 전용):
  요청-응답 패턴
  TokenValidateRequest → TokenValidateResponse

연결 2 (SlotNotifySystem 전용):
  일방향 전송 패턴
  SlotReleased → (응답 없음)
  GameServerHeartbeat → (응답 없음)

두 연결 모두 서버 시작 시 수립, 종료 시 정리.
localhost 통신이므로 연결 2개의 리소스 비용은 무시 가능.
```

---

## 테스트 요구사항

### EditMode Test

- GoInGameRequestRpc에 AuthToken 필드가 존재하는지 구조 검증
- RoomSessionInfo, TokenValidatedTag 컴포넌트 생성 검증

### PlayMode Test

- 토큰 없이 :7979 직접 접속 → Disconnect 확인
- 유효 토큰으로 접속 → Hero 생성 확인
- 만료 토큰(60초 초과)으로 접속 → Disconnect 확인
- 클라이언트 연결 끊김 → SlotReleased 전송 확인 (룸 서버 로그)

---

## 검증 방법

1. 토큰 없이 게임 서버 직접 접속 시 즉시 Disconnect
2. 룸 서버에서 GameStart 받은 후 유효 토큰으로 게임 서버 접속 성공
3. 연결 끊김 시 룸 서버에 SlotReleased 메시지 수신 확인
4. 30초마다 GameServerHeartbeat 전송 확인

## 완료 기준

- [x] GoInGameRequestRpc에 AuthToken 필드 추가됨
- [x] GoInGameClientSystem이 RoomAuthState에서 토큰 읽어 전송
- [x] TokenValidationSystem이 :8081에서 토큰 검증 수행
- [x] 유효 토큰만 GoInGameServerSystem 통과 (Hero 생성)
- [x] 무효/빈 토큰 시 Disconnect 처리
- [x] SlotNotifySystem이 연결 끊김 시 SlotReleased 전송
- [x] GameServerHeartbeat 30초 주기 전송
- [x] 전체 프로젝트 컴파일 성공
