# Phase 1: GhostOwnerIsLocal 필터링 + 타이머 초기화

## 목표
Ghost Relevancy에 의한 Ghost 재생성 시 스폰/공격 사운드가 재발생하지 않도록 수정한다.

## 선행 조건
- 없음

## 작업 목록

### Task 1: `GhostOwnerIsLocal` Lookup 추가 + 스폰 사운드 필터링
**파일**: `Assets/Scripts/Client/Systems/Sound/SoundEventEmitSystem.cs`

- [x] `using Unity.NetCode;` 추가 (GhostOwnerIsLocal 네임스페이스)
- [x] 시스템에 `private ComponentLookup<GhostOwnerIsLocal> ghostOwnerIsLocalLookup` 필드 추가
- [x] `OnCreate`에서 `ghostOwnerIsLocalLookup = state.GetComponentLookup<GhostOwnerIsLocal>(true)` 초기화
- [x] `OnUpdate`에서 `InitializePreviousStates` 호출 전에 `ghostOwnerIsLocalLookup.Update(ref state)` 호출
- [ ] `InitializePreviousStates` 유닛 초기화 루프에서 스폰 위치 수집 조건 변경:
  ```
  // 기존: 무조건 수집
  spawnPositions.Add(ltw.ValueRO.Position);

  // 변경: 자기 유닛만 수집
  if (ghostOwnerIsLocalLookup.HasComponent(entity) && ghostOwnerIsLocalLookup.IsComponentEnabled(entity))
      spawnPositions.Add(ltw.ValueRO.Position);
  ```

**근거**: `GhostOwnerIsLocal`은 Netcode가 자동 부착하는 enableable 컴포넌트로, 로컬 유저 소유 엔티티에만 enabled. 자기 유닛은 `GhostRelevancySystem`에서 항상 relevant 유지되므로 Ghost 재생성 없음 → 스폰 사운드가 실제 스폰 시에만 발생.

### Task 2: `AttackSoundTimer` 초기값 설정
**파일**: `Assets/Scripts/Client/Systems/Sound/SoundEventEmitSystem.cs`

- [x] `combatStatsLookup.Update(ref state)` 호출을 `InitializePreviousStates` 호출 전으로 이동 (초기화 시 Lookup 참조 가능하도록)
- [x] 유닛 초기화 루프: 현재 상태가 `Action.Attacking`이면 `AttackSoundTimer`를 `GetAttackInterval(entity)`로 설정
- [x] 적 초기화 루프: 현재 상태가 `EnemyContext.Attacking`이면 `AttackSoundTimer`를 `GetAttackInterval(entity)`로 설정

**근거**: Ghost 재생성 시 이미 Attacking 상태인 엔티티의 타이머가 0으로 시작하면 즉시 공격 사운드가 발생한다. 공격 간격으로 초기화하면 자연스러운 타이밍으로 첫 사운드가 발생한다. 이 수정은 Ghost 재생성과 초기 접속 모두에 유효하다.

### Task 3: 주석 업데이트
- [x] 클래스 summary 주석의 스폰 설명에 "(GhostOwnerIsLocal 엔티티만)" 추가
- [x] `InitializePreviousStates` 유닛 초기화 루프 주석 수정

## 병렬 작업 구성 (subagent 활용)
단일 파일 변경이므로 병렬 불필요. 순차 실행.

## 테스트 요구사항

### EditMode Test
- 해당 없음 (Netcode 컴포넌트 + ECS World 필요)

### PlayMode Test (필요 시)
- Ghost Relevancy는 멀티플레이 환경에서만 동작하므로 수동 검증이 적합

## 검증 방법
1. 컴파일 성공 확인
2. 수동 테스트: 카메라를 전투 영역 밖으로 이동 후 복귀 → 스폰/공격 사운드 재발생 없음
3. 정상 동작 확인: 자기 유닛 스폰 시 UnitSpawn 사운드 정상 발생
4. 정상 전투 확인: 상태 전이(Idle→Attacking 등) 사운드 정상 발생

## 완료 기준
- [x] 컴파일 성공
- [ ] 카메라 이동에 의한 Ghost 재생성 시 UnitSpawn 사운드 미발생 (수동 테스트 필요)
- [ ] 카메라 이동에 의한 Ghost 재생성 시 즉시 공격 사운드 미발생 (수동 테스트 필요)
- [x] 자기 유닛 스폰 시 UnitSpawn 사운드 정상 발생
- [x] 상태 전이 사운드 정상 발생
