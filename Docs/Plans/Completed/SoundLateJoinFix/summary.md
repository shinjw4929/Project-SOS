# SoundLateJoinFix 계획 요약

## 문제 정의
Ghost Relevancy에 의한 Ghost 재생성 시 스폰/공격 사운드가 재발생하는 버그. 카메라 이동으로 뷰포트 밖 엔티티가 irrelevant → relevant 전환될 때 발생.

## Phase 구성
- **Phase 1** (단일): `GhostOwnerIsLocal` 필터링으로 자기 유닛만 스폰 사운드 + AttackSoundTimer 공격 간격 초기화

## 예상 영향 범위
- `Assets/Scripts/Client/Systems/Sound/SoundEventEmitSystem.cs` 1개 파일

## 자동 리뷰
- 1회차에 승인 후 계획 수정 (근본 원인 재분석: 초기 동기화 → Ghost Relevancy).
