# System Execution Flow

```
[1. 입력] GhostInputSystemGroup (Client)
    UserSelectionInputUpdateSystem → EntitySelectionSystem → SelectedEntityInfoUpdateSystem
        ├─ UnitCommandInputSystem → RPC 전송 (MoveRequestRpc/AttackRequestRpc)
        │      ↓
        │  StructurePlacementInputSystem → RPC 전송 (BuildRequestRpc/BuildMoveRequestRpc)
        └─ StructureCommandInputSystem

[2. 공간 분할] SpatialPartitioningGroup (Server, OrderFirst)
    SpatialMapBuildSystem → TargetingMap (10.0f) + MovementMap (3.0f) → SpatialMaps 싱글톤

[3. 명령 처리] SimulationSystemGroup (Server)
    Handle*RequestSystem → MovementGoal, AggroTarget, Intent 설정

[4. 타겟팅] SimulationSystemGroup (Server)
    UnifiedTargetingSystem (SpatialMaps.TargetingMap 사용)
        ├─ EnemyTargetJob → 적→아군 타겟팅
        └─ UnitAutoTargetJob → 유닛→적 자동 감지

[5. 이동] SimulationSystemGroup (Server)
    GridObstacleResponseSystem → GridObstacleCleanupSystem
    → FlowFieldSystem (Phase0→Collect→Compute×8→Apply) → FlowFieldSteeringSystem
    → PredictedMovementSystem (SpatialMaps.MovementMap + GridCell.IsPathBlocked 사용)
    → MovementArrivalSystem → BuildArrivalSystem (도착 시 건설)

[6. 전투] FixedStepSimulationSystemGroup (Server)
    CombatDamageSystem → MeleeAttackSystem → RangedAttackSystem → DamageApplySystem
    ※ 모든 데미지는 DamageEvent 버퍼를 통해 DamageApplySystem에서 일괄 적용 + 적 킬 카운트

[6.5. 애니메이션 상태] SimulationSystemGroup (Server, UpdateAfter FixedStepSimulationSystemGroup)
    VATAnimationStateUpdateSystem (UnitActionState/EnemyState → VATAnimationState.CurrentClipIndex 갱신, Ghost 동기화)

[7. 정리] SimulationSystemGroup (Server)
    HeroDeathDetectionSystem → ServerDeathSystem (DeathDetectionJob: Dying+DeathTimer 부착, DeathTimerJob: 타이머 만료 시 파괴) → GridObstacleCleanupSystem, TechStateRecalculateSystem

[7.5. 네트워크 최적화] SimulationSystemGroup (Server)
    UpdateConnectionPositionSystem (CameraPositionRpc 수신) → GhostRelevancySystem (뷰포트 AABB × 1.3 밖 적/다른 유저 유닛 Ghost 전송 제외, 자기 유닛 항상 relevant)
    MinimapDataBroadcastSystem → MinimapBatchRpc 분산 전송 (적/유닛 위치+teamId, 단일 float3 파이프라인)

[7.6. 미니맵] SimulationSystemGroup (Client)
    MinimapDataReceiveSystem → MinimapDataState (Double buffer 스왑, 적/유닛 수 카운트)
    MinimapRenderer (MonoBehaviour) → Texture2D 렌더링

[8. 후처리] LateSimulationSystemGroup
    GridOccupancyEventSystem, PopulationApplySystem (인구수 변경 이벤트 소비)

[9. Transform] TransformSystemGroup
    CarriedResourceFollowSystem (Scale 기반 가시성 토글)

[9.5. 커맨드 마커] SimulationSystemGroup (Client)
    CommandMarkerPoolInitSystem → 타입당 4개 풀 초기화 (1회 실행)
    CommandMarkerFadeSystem → Scale 선형 감소 + 수명 만료 시 Scale=0 (풀링)

[9.7. 애니메이션+사운드] SimulationSystemGroup (Client)
    VATAnimationInitSystem (새 메시 엔티티에 VATAnimParam/VATAnimTarget/PreviousClipIndex 부착)
    → VATAnimationPlaybackSystem (VATAnimParam 계산, IJobEntity [BurstCompile])
    → SoundEventEmitSystem (상태 변화 + AttackSpeed 타이머 → SoundEvent 버퍼, 공격 반복/사망/스폰)
    → EntityTiltSystem (Attacking swing-return + Dying 전방 기울임, PostTransformMatrix, VAT 유무 무관, IJobEntity [BurstCompile])
    → TeamColorSystem (기존)
    → SoundManager (MonoBehaviour, 매 프레임 SoundEvent 버퍼 소비 + AudioSource 풀 재생)

[10. 렌더링] PresentationSystemGroup (Client)
    StructurePreviewUpdateSystem, EnemyHpTextPresentationSystem, CameraSystem
```

## 핵심 의존성

- `UnifiedTargetingSystem`: UpdateAfter `SpatialPartitioningGroup`, `HandleAttackRequestSystem`
- `DamageApplySystem`: UpdateAfter `MeleeAttackSystem` (DamageEvent 버퍼 소비)
- `FlowFieldSystem`: BFS 기반 Flow Field 계산 (LRU 캐시 32×2풀, 8 IJob 병렬), Grid 기반 passability
- `SpatialMapBuildSystem`: Persistent 맵 + Job 기반 Clear → dependency chain으로 동기화 (CompleteDependency 불필요)
- `GhostRelevancySystem`: UpdateAfter `UpdateConnectionPositionSystem` (Ghost Relevancy AABB 필터링, 적+다른 유저 유닛, 자기 유닛 skip, ViewHalfExtent × 1.3/1.15)
