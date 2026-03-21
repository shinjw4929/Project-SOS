# Phase 3: ECS 시스템 (서버 + 클라이언트)

**전제 조건**: Phase 1

---

## 서버: VATAnimationStateUpdateSystem

**신규 파일**: `Server/Systems/Animation/VATAnimationStateUpdateSystem.cs`

- 그룹: `SimulationSystemGroup`, **UpdateAfter: `FixedStepSimulationSystemGroup`**
- UnitActionState/EnemyState 변화 → VATAnimationState.CurrentClipIndex + AnimStartTime 갱신

### 클립 매핑

유닛: Idle→0, Moving→1, Working→2, Attacking→3, Dying→4, Dead→5, Disabled→6
적: Idle/Dormant→0, Wandering/Chasing→1, Attacking→2, Dying→3, Dead→4, Disabled→5

### UpdateAfter 근거

UnitActionState는 SimulationSystemGroup(WorkerGatheringSystem 등)과 FixedStepSimulationSystemGroup(MeleeAttackSystem) 양쪽에서 변경됨. FixedStep 이후 실행해야 양쪽 변경 모두 캡처 가능.

---

## 클라이언트: VATAnimationInitSystem

**신규 파일**: `Client/Systems/Animation/VATAnimationInitSystem.cs`

- 그룹: `SimulationSystemGroup`, UpdateBefore: `VATAnimationPlaybackSystem`
- TeamColorSystem Phase 1 패턴 활용 (Parent 체인 탐색)
- 새 메시 엔티티 감지 → VATAnimParam 부착

---

## 클라이언트: VATAnimationPlaybackSystem

**신규 파일**: `Client/Systems/Animation/VATAnimationPlaybackSystem.cs`

- 그룹: `SimulationSystemGroup`, UpdateBefore: `TeamColorSystem`
- **단일 쿼리**: VATAnimationState에서 CurrentClipIndex + AnimStartTime 읽기 (유닛/적 공통)
- VATClipLibrary(BlobAssetReference)에서 클립 메타데이터 조회
- ElapsedTime - AnimStartTime으로 진행률 계산 → VATAnimParam.Value 갱신
- 루프 클립: fmod, 비루프: clamp (마지막 프레임 유지)
- IJobEntity + [BurstCompile] + ScheduleParallel

---

## 체크리스트

- [ ] `VATAnimationStateUpdateSystem` 구현 (서버)
- [ ] 유닛 클립 매핑 (Idle→0, Moving→1, Working→2, Attacking→3, Dying→4, Dead→5, Disabled→6)
- [ ] 적 클립 매핑 (Idle/Dormant→0, Wandering/Chasing→1, Attacking→2, Dying→3, Dead→4, Disabled→5)
- [ ] `VATAnimationInitSystem` 구현 (클라이언트, Parent 체인 탐색 패턴)
- [ ] `VATAnimationPlaybackSystem` 구현 (클라이언트, IJobEntity + BurstCompile)
- [ ] 진행률 계산: 루프=fmod, 비루프=clamp
- [ ] 시스템 의존성 설정 (UpdateInGroup, UpdateAfter, UpdateBefore)
