# BGM 시스템 계획 요약

## 문제 정의
현재 게임에 BGM이 없어 모든 화면(시작/로비/인게임)이 무음 상태. SFX만 존재.

## Phase 구성
- **Phase 1**: BGMManager MonoBehaviour 구현 (단일 Phase)
  - `Assets/Scripts/Client/Controller/Sound/BGMManager.cs` 신규 생성
  - RoomClientState 기반 3종 BGM 전환 (Title / Lobby / InGame)
  - 2-AudioSource 크로스페이드, Time.unscaledDeltaTime 사용
  - 기존 시스템(SoundManager, RoomClient) 변경 없음

## 예상 영향 범위
- 클라이언트 전용, 파일 1개 신규 생성
- 기존 코드 변경 없음 (완전 독립 추가)

## 자동 리뷰 통과 여부
1회차에 승인. DontDestroyOnLoad 추가 1건 반영 완료.
