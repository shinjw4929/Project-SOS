# 네트워크 아키텍처

## 접속 흐름 (Room Server Integration)

```
[1. 로비] RoomClient (MonoBehaviour, TCP :8080)
    앱 시작 → RoomClient가 룸 서버에 TCP 연결
    → 로비/대기실 UI → GameStart 메시지 수신
    → AuthToken + SessionId + ServerAddress 획득

[2. Netcode 연결] NetcodeConnectionUtil (static)
    GameStart 수신 → NetcodeConnectionUtil.Connect(serverAddress, port)
    → Netcode ClientWorld 수동 연결 (AutoConnect 비활성화)
    → RoomAuthState 싱글톤에 AuthToken + SessionId 저장

[3. 게임 진입] GoInGameClientSystem (Client)
    GoInGameRequestRpc 전송 (AuthToken 포함)

[4. 토큰 검증] TokenValidationSystem (Server, UpdateBefore: GoInGameServerSystem)
    GoInGameRequestRpc 수신 → RoomTokenValidator로 룸 서버 :8081 TCP 검증
    → 검증 성공: TokenValidatedTag + RoomSessionInfo(SessionId, UserId) 부착
    → 검증 실패: 연결 거부

[5. Hero 생성] GoInGameServerSystem (Server, WithAll<TokenValidatedTag> 필터)
    TokenValidatedTag가 있는 Connection만 Hero 생성 처리

[6. 슬롯 관리] SlotNotifySystem (Server)
    연결 끊김 감지 → SlotReleased 메시지 → 룸 서버 :8081 TCP
    30초 간격 하트비트 전송
```

**프로토콜**: Protobuf (Sos.Room 네임스페이스) + 4byte LE length-prefix 프레이밍 (`ProtobufFraming`)

## Room Server Token Validation Pattern

- **클라이언트**: `RoomClient`(MonoBehaviour)가 룸 서버 TCP :8080 연결 → `GameStart` 메시지로 토큰/세션/서버주소 수신 → `NetcodeConnectionUtil`로 Netcode 수동 연결 → `GoInGameRequestRpc`에 토큰 포함
- **서버**: `TokenValidationSystem`(managed SystemBase)이 `RoomTokenValidator`로 룸 서버 :8081 TCP 검증 → 성공 시 `TokenValidatedTag` + `RoomSessionInfo` 부착 → `GoInGameServerSystem`은 `WithAll<TokenValidatedTag>` 필터로 검증된 클라이언트만 Hero 생성
- **슬롯 관리**: `SlotNotifySystem`이 연결 끊김 감지 시 `SlotReleased` 전송 + 30초 하트비트

## Network RPCs

`MoveRequestRpc`, `AttackRequestRpc`, `BuildRequestRpc`, `BuildMoveRequestRpc`, `GatherRequestRpc`, `ReturnResourceRequestRpc`, `ProduceUnitRequestRpc`, `SelfDestructRequestRpc`, `CameraPositionRpc`, `NotificationRpc`, `HeroDeathRpc`, `GameOverRpc`, `MinimapBatchRpc`
