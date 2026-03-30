# 룸 서버 RoomClosed Reject 처리 버그 수정

## 문제

방장이 방을 나가면 서버가 남은 클라이언트에게 `Reject(RoomClosed)`를 전송한다. 그런데 클라이언트가 에러 패널("Room has been closed")만 표시하고 상태는 InRoom에 머물러 있어서, Retry 버튼을 누르면 이미 사라진 방의 대기실 화면이 그대로 노출되는 문제가 있었다.

### 원인

- `RoomClient.DispatchEnvelope`: `Reject`를 `OnRejected` 이벤트로만 전달하고 상태 전환 없음
- `RoomUIController.HandleRejected`: 에러 패널만 표시, 상태 변경 없음
- `RoomUIController.OnRetryClicked`: 재접속 시도하지만 이미 연결 상태(InRoom)라 `ConnectToRoomServer`가 무시됨 → 에러 패널만 닫히고 죽은 방 화면 노출

## 수정 내용

### RoomClient.cs
- `HandleReject` 메서드 신규 추가
- `Reject(RoomClosed)` + InRoom 상태일 때 `State = Lobby`로 전환
- 상태 전환 → `OnStateChanged(Lobby)` 발행 → UI 로비 패널 전환 + 방 목록 자동 재요청(0.5초 후)

### RoomUIController.cs
- `HandleRejected`: `RoomClosed` 사유는 상태 전환으로 처리되므로 에러 패널 표시 생략
- `OnRetryClicked`: 이미 Lobby/InRoom 연결 상태면 에러 패널만 닫고 재접속 시도 안 함
- `PopulateUserList`: 호스트는 레디 버튼 비표시, 시작 버튼만 표시 (`readyButton.SetActive(!isHost)`)

## 변경 파일

- `Assets/Scripts/Client/Network/RoomClient.cs`
- `Assets/Scripts/Client/Controller/UI/RoomUIController.cs`
