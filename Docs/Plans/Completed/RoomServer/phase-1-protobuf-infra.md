# Phase 1: Protobuf 환경 구성 + 연결 인프라 전환

## 목표

- Google.Protobuf 런타임을 Unity에 통합하고 room.proto에서 C# 코드를 생성한다.
- 기존 자동 접속 방식을 비활성화하고 수동 연결 인프라를 구축한다.

## 선행 조건

- C++ 룸 서버(Project-SOS-Backend)가 빌드/실행 가능한 상태
- `proto/room.proto`가 확정된 상태 (현재 확정됨)

---

## 작업 목록

### Task 1: Google.Protobuf Unity 통합

- [ ] `Google.Protobuf.dll`을 `Assets/Plugins/Protobuf/`에 배치
  - NuGet에서 `Google.Protobuf` 최신 안정 버전 다운로드 후 dll 추출
  - `Assets/Plugins/` 하위 배치 시 전체 asmdef(Client, Server, Shared)에 auto-reference 적용
  - 개별 asmdef에 Override References 추가 불필요
  - 대안: NuGetForUnity 패키지 매니저 사용
- [ ] `Google.Protobuf.dll`이 Unity Editor + Standalone 빌드에서 참조되는지 확인
- [ ] Client.asmdef, Server.asmdef, Shared.asmdef 모두에서 Protobuf 타입 접근 가능한지 확인

### Task 2: Protobuf C# 코드 생성

- [ ] `protoc` 설치 확인 (또는 NuGet `Grpc.Tools`에서 추출)
- [ ] 코드 생성 명령:
  ```bash
  protoc --proto_path=<backend>/proto --csharp_out=<unity>/Assets/Scripts/Shared/Network/Generated room.proto
  ```
- [ ] 생성된 C# 파일이 Unity에서 컴파일되는지 확인
- [ ] `Shared/Network/Generated/` 폴더는 Shared.asmdef 범위 내에 배치
  - Client(RoomClient)와 Server(RoomTokenValidator, SlotNotifyClient) 양쪽에서 참조 필요
  - Google.Protobuf DLL은 Assets/Plugins/Protobuf/에 배치되어 전체 asmdef에서 auto-reference

### Task 2.5: Protobuf DLL 참조 확인

- [ ] `Assets/Plugins/Protobuf/` 배치로 auto-reference 적용 확인
  - Shared.asmdef, Client.asmdef, Server.asmdef 모두 Override References 미사용 시 자동 참조
  - 만약 asmdef에 Override References가 활성화되어 있으면 각 asmdef에 개별 참조 추가 필요
- [ ] 컴파일 확인 (순환 참조 없는지 검증)

### Task 3: 프레이밍 유틸리티 구현

- [ ] `ProtobufFraming.cs` 생성 (`Shared/Network/`)
  - `static byte[] Frame(Envelope envelope)` — Envelope 직렬화 + 4byte LE length prefix 추가
  - `static bool TryDeframe(byte[] buffer, ref int offset, out Envelope envelope)` — 수신 버퍼에서 완전한 메시지 추출
  - 최대 메시지 크기: 1MB (백엔드와 동일)
- [ ] 단위 테스트: Frame → Deframe 왕복 검증

### Task 4: 자동 연결 비활성화

- [ ] `GameBootStrap.cs` 수정:
  ```csharp
  // 변경 전
  AutoConnectPort = 7979;
  // 변경 후
  AutoConnectPort = 0;  // 룸 서버 연동 후 수동 연결
  ```
- [ ] 변경 후 앱 실행 시 자동 접속이 발생하지 않는지 확인

### Task 5: 수동 연결 유틸리티

- [ ] `NetcodeConnectionUtil.cs` 생성 (`Client/Network/`)
  ```
  역할: GameStart 메시지의 host:port를 받아 Netcode 연결을 트리거
  API: static void Connect(World clientWorld, string host, ushort port)
  방법: NetworkStreamDriver를 통해 Connect(NetworkEndpoint) 호출
  참고: Unity.NetCode.NetworkStreamDriver API
  ```
- [ ] 연결 성공/실패 이벤트 콜백 구조 설계

---

## 병렬 작업 구성 (subagent 활용)

| Agent | 작업 내용 | 의존성 |
|-------|----------|--------|
| Agent A | Task 1 + Task 2 (Protobuf 환경) | 없음 |
| Agent B | Task 4 + Task 5 (연결 인프라) | 없음 |
| Main | Task 3 (프레이밍 유틸리티) | Task 1 완료 후 |

---

## 테스트 요구사항

### EditMode Test

- `ProtobufFraming.Frame()` → 올바른 4byte LE prefix + 직렬화 바이트
- `ProtobufFraming.TryDeframe()` → 부분 수신, 완전 수신, 다중 메시지 케이스
- Envelope oneof 필드별 직렬화/역직렬화 검증

### PlayMode Test

- 앱 시작 시 자동 접속이 발생하지 않음 확인 (AutoConnectPort = 0)

---

## 검증 방법

1. Unity 에디터에서 컴파일 에러 없음
2. Generated C# 코드에서 `Envelope`, `CreateRoomRequest` 등 클래스 접근 가능
3. `ProtobufFraming` 유닛 테스트 전체 통과
4. 에디터 Play → 자동 접속 발생하지 않음

## 완료 기준

- [x] Google.Protobuf dll이 Unity에서 참조됨 (Assets/Plugins/Protobuf/ 배치, 전체 asmdef auto-reference)
- [x] room.proto에서 생성된 C# 코드가 Shared/Network/Generated/에 배치되고 컴파일됨
- [x] ProtobufFraming 유틸리티 구현 + 테스트 작성 (EditMode 테스트 7건)
- [x] AutoConnectPort = 0 적용 완료
- [x] NetcodeConnectionUtil 구현 완료
- [x] 전체 프로젝트 컴파일 성공
