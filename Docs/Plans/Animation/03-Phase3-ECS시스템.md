# Phase 3: ECS 시스템 (서버 + 클라이언트)

**전제 조건**: Phase 1

---

## 서버: VATAnimationStateUpdateSystem

**신규 파일**: `Assets/Scripts/Server/Systems/Animation/VATAnimationStateUpdateSystem.cs`

```csharp
[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(FixedStepSimulationSystemGroup))]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
partial struct VATAnimationStateUpdateSystem : ISystem
```

### 클립 매핑 로직

UnitActionState/EnemyState의 이전 값을 비교하여, 변화 시에만 VATAnimationState를 갱신한다. `AnimStartTime`에 현재 `ElapsedTime`을 기록하여 클라이언트에서 정확한 진행률을 계산할 수 있게 한다.

```csharp
// 유닛 클립 매핑 (Action enum → clip index)
static byte GetUnitClipIndex(Action action) => action switch
{
    Action.Idle     => 0,
    Action.Moving   => 1,
    Action.Working  => 2,
    Action.Attacking => 3,
    Action.Dying    => 4,
    Action.Dead     => 5,
    Action.Disabled => 6,
    _               => 0,  // 폴백: Idle
};

// 적 클립 매핑 (EnemyContext enum → clip index)
static byte GetEnemyClipIndex(EnemyContext ctx) => ctx switch
{
    EnemyContext.Idle      => 0,
    EnemyContext.Dormant   => 0,  // Idle과 동일
    EnemyContext.Wandering => 1,
    EnemyContext.Chasing   => 1,  // Wandering과 동일 (이동 모션)
    EnemyContext.Attacking => 2,
    EnemyContext.Dying     => 3,
    EnemyContext.Dead      => 4,
    EnemyContext.Disabled  => 5,
    _                      => 0,
};
```

### OnUpdate 구현

```csharp
[BurstCompile]
public void OnUpdate(ref SystemState state)
{
    var elapsedTime = (float)SystemAPI.Time.ElapsedTime;

    // 유닛 클립 인덱스 갱신
    foreach (var (actionState, animState) in
        SystemAPI.Query<RefRO<UnitActionState>, RefRW<VATAnimationState>>())
    {
        byte newClip = GetUnitClipIndex(actionState.ValueRO.CurrentAction);
        if (newClip == animState.ValueRO.CurrentClipIndex) continue;

        animState.ValueRW.CurrentClipIndex = newClip;
        animState.ValueRW.AnimStartTime = elapsedTime;
    }

    // 적 클립 인덱스 갱신
    foreach (var (enemyState, animState) in
        SystemAPI.Query<RefRO<EnemyState>, RefRW<VATAnimationState>>())
    {
        byte newClip = GetEnemyClipIndex(enemyState.ValueRO.CurrentContext);
        if (newClip == animState.ValueRO.CurrentClipIndex) continue;

        animState.ValueRW.CurrentClipIndex = newClip;
        animState.ValueRW.AnimStartTime = elapsedTime;
    }
}
```

### 변화 감지 전략

이전 프레임의 `CurrentClipIndex`와 새로 계산된 인덱스를 비교. 다를 때만 VATAnimationState를 갱신하여 불필요한 Ghost 전송을 방지한다.

### UpdateAfter 근거

UnitActionState는 두 곳에서 변경됨:
1. SimulationSystemGroup (WorkerGatheringSystem 등)
2. FixedStepSimulationSystemGroup (MeleeAttackSystem, RangedAttackSystem)

`UpdateAfter(FixedStepSimulationSystemGroup)`으로 배치하면 전투 시스템(FixedStep 내부) 이후에 실행됨. SimulationSystemGroup의 다른 시스템(WorkerGatheringSystem 등)은 FixedStep보다 먼저 실행되므로, 이 배치로 양쪽 변경을 모두 캡처 가능. 참고: HeroDeathDetectionSystem/ServerDeathSystem은 UnitActionState를 변경하지 않고 엔티티 파괴만 수행하므로 순서 제약 불필요.

### Dying/Dead 상태 주의사항

ClientDeathSystem이 체력 <= 0일 때 `DisableRendering`을 추가하여 렌더링을 즉시 비활성화한다. 따라서 Dying 애니메이션 클립은 실제로 재생되지 않을 가능성이 높다.

- Dying/Dead/Disabled 상태에 대한 클립 인덱스 매핑은 유지한다 (폴백 안전성)
- 사망 연출 애니메이션이 필요하면, ClientDeathSystem 수정이 선행되어야 한다 (Dying 상태에서 일정 시간 렌더링 유지)
- 베이킹 시 Dying/Dead 클립이 없는 경우: `math.clamp`로 바운드 보호되지만 잘못된 클립이 재생됨 → 베이킹 단계에서 모든 클립 존재 여부 검증 필요

---

## 클라이언트: VATAnimationInitSystem

**신규 파일**: `Assets/Scripts/Client/Systems/Animation/VATAnimationInitSystem.cs`

```csharp
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(VATAnimationPlaybackSystem))]
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
partial struct VATAnimationInitSystem : ISystem
```

### 초기화 패턴 (TeamColorSystem 참조)

```csharp
// 새 메시 엔티티 감지: MaterialMeshInfo가 있지만 VATAnimParam이 아직 없는 엔티티
foreach (var (parent, entity) in
    SystemAPI.Query<RefRO<Parent>>()
        .WithAll<MaterialMeshInfo>()
        .WithNone<VATAnimParam>()
        .WithEntityAccess())
{
    // Parent 체인 탐색 (최대 10 깊이) → VATAnimationState를 가진 루트 엔티티 탐색
    Entity rootEntity = FindVATAncestor(parent.ValueRO.Value, ref state);
    if (rootEntity == Entity.Null) continue;

    // ECB로 VATAnimParam + VATAnimTarget + PreviousClipIndex 부착
    ecb.AddComponent(entity, new VATAnimParam { Value = float4.zero });
    ecb.AddComponent(entity, new VATAnimTarget { RootEntity = rootEntity });
    ecb.AddComponent(entity, new PreviousClipIndex { Value = 0 });  // Phase 5 사운드 변화 감지용
}

// 주의: ECB Playback은 프레임 끝에 처리됨.
// InitSystem(UpdateBefore) → PlaybackSystem 순서이므로,
// 새로 부착된 VATAnimParam은 다음 프레임부터 PlaybackSystem에서 계산됨.
// 첫 프레임은 float4.zero 상태 → 셰이더에서 프레임 0 위치 표시 (바인드포즈, 자연스러움).
```

`VATAnimTarget`은 TeamColorTarget과 동일 패턴으로, 메시 엔티티가 자신의 루트(VATAnimationState 보유) 엔티티를 참조하는 컴포넌트. 정의는 Phase 1 참조 (`Assets/Scripts/Client/Component/Animation/VATAnimTarget.cs`).

---

## 클라이언트: VATAnimationPlaybackSystem

**신규 파일**: `Assets/Scripts/Client/Systems/Animation/VATAnimationPlaybackSystem.cs`

```csharp
[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(TeamColorSystem))]
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
partial struct VATAnimationPlaybackSystem : ISystem
```

### VATAnimParam 계산 알고리즘

```csharp
[BurstCompile]
partial struct VATPlaybackJob : IJobEntity
{
    [ReadOnly] public ComponentLookup<VATAnimationState> AnimStateLookup;
    [ReadOnly] public ComponentLookup<VATClipLibrary> ClipLibraryLookup;
    public double ElapsedTime;

    void Execute(in VATAnimTarget target, ref VATAnimParam param)
    {
        if (target.RootEntity == Entity.Null) return;
        if (!AnimStateLookup.TryGetComponent(target.RootEntity, out var animState)) return;
        if (!ClipLibraryLookup.TryGetComponent(target.RootEntity, out var clipLib)) return;

        ref var blobData = ref clipLib.Value.Value;
        int clipIndex = math.clamp(animState.CurrentClipIndex, 0, blobData.Clips.Length - 1);
        ref var clip = ref blobData.Clips[clipIndex];

        float elapsed = (float)(ElapsedTime - animState.AnimStartTime);
        float clipDuration = clip.RowCount / clip.Fps;

        float normalizedTime;
        if (clip.Loop)
            normalizedTime = math.fmod(elapsed, clipDuration) / clipDuration;
        else
            normalizedTime = math.saturate(elapsed / clipDuration);

        float textureHeight = blobData.TextureHeight;
        param.Value = new float4(
            normalizedTime,                           // x: 클립 내 진행률 (0~1)
            clip.StartRow / textureHeight,            // y: 클립 시작 V좌표 (정규화)
            clip.RowCount / textureHeight,            // z: 클립 높이 (정규화)
            0                                         // w: reserved
        );
    }
}
```

---

## 체크리스트

- [ ] `VATAnimationStateUpdateSystem` 구현 (서버, ISystem)
- [ ] 유닛 클립 매핑: Action enum → clip index (switch expression)
- [ ] 적 클립 매핑: EnemyContext enum → clip index (switch expression)
- [ ] 변화 감지: 이전 CurrentClipIndex와 비교, 변화 시에만 갱신
- [ ] Dying/Dead 클립 매핑 유지 + 베이킹 시 클립 존재 여부 검증 (Phase 2 연계)
- [ ] `VATAnimationInitSystem` 구현 (클라이언트, Parent 체인 탐색 패턴)
- [ ] 쿼리 필터: `WithAll<MaterialMeshInfo>`, `WithNone<VATAnimParam>`
- [ ] ECB Playback 타이밍 확인 (새 컴포넌트는 다음 프레임부터 유효)
- [ ] `VATAnimationPlaybackSystem` 구현 (클라이언트, IJobEntity + BurstCompile)
- [ ] VATAnimParam.Value 계산: normalizedTime, clipStartRow(정규화), clipRowCount(정규화)
- [ ] 진행률 계산: 루프=fmod, 비루프=saturate
- [ ] BlobAssetReference 접근: `clipLib.Value.Value.Clips[clipIndex]`
- [ ] clipIndex 바운드 보호: `math.clamp(clipIndex, 0, Clips.Length - 1)`
- [ ] 시스템 의존성 설정 (UpdateInGroup, UpdateAfter, UpdateBefore, WorldSystemFilter)
