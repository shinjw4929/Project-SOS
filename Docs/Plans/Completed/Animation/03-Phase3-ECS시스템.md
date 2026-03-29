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

### VAT 적용 대상

에셋 조사 결과([00-에셋조사.md](00-에셋조사.md)), VAT 적용 가능 프리팹은 **3종**:
- **Hero** (LittleSquirrel): Idle, Walk, Run, Eat 클립 보유 → Idle/Moving/Working 매핑
- **EnemySmall/EnemyFlying** (Ghost): ghost_idle, ghost_run, ghost_attack, ghost_dissolve 등 6 Take → Idle/Moving/Attacking/Dying 매핑

VAT 미적용 프리팹 5종(Worker/Striker/Tank/Archer: Origami 정적 메시, EnemyBig: 걷기 클립 없음)은 VATAnimationAuthoring 미부착 → VATAnimationState 컴포넌트 없음 → 이 시스템 쿼리에서 자연 제외.

### 클립 매핑 로직

UnitActionState/EnemyState의 이전 값을 비교하여, 변화 시에만 VATAnimationState를 갱신한다. `AnimStartTime`에 현재 `ElapsedTime`을 기록하여 클라이언트에서 정확한 진행률을 계산할 수 있게 한다.

클립 인덱스는 VATClipDataAsset의 베이킹 순서와 일치해야 한다. 에셋에 해당 클립이 없는 경우 Idle(0)로 폴백하며, `math.clamp`로 바운드 보호.

```csharp
// 유닛 클립 매핑 — Hero(LittleSquirrel) 기준
// 베이킹 순서: [0]Idle, [1]Walk, [2]Eat
// Attack/Death 클립 부재 → Idle(0) 폴백
static byte GetUnitClipIndex(Action action) => action switch
{
    Action.Idle      => 0,
    Action.Moving    => 1,  // Walk
    Action.Working   => 2,  // Eat
    Action.Attacking => 0,  // 클립 부재 → Idle 폴백 (전투 기울임으로 대체)
    Action.Dying     => 0,  // 클립 부재 → Idle 폴백
    Action.Dead      => 0,
    Action.Disabled  => 0,
    _                => 0,
};

// 적 클립 매핑 — Ghost(GhostCharacter_Free) 기준
// 베이킹 순서: [0]ghost_idle, [1]ghost_run, [2]ghost_attack, [3]ghost_dissolve
static byte GetEnemyClipIndex(EnemyContext ctx) => ctx switch
{
    EnemyContext.Idle      => 0,  // ghost_idle
    EnemyContext.Dormant   => 0,
    EnemyContext.Wandering => 1,  // ghost_run
    EnemyContext.Chasing   => 1,  // ghost_run
    EnemyContext.Attacking => 2,  // ghost_attack
    EnemyContext.Dying     => 3,  // ghost_dissolve
    EnemyContext.Dead      => 3,
    EnemyContext.Disabled  => 0,
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
        byte newClip = GetUnitClipIndex(actionState.ValueRO.State);
        if (newClip == animState.ValueRO.CurrentClipIndex) continue;

        animState.ValueRW.CurrentClipIndex = newClip;
        animState.ValueRW.AnimStartTime = elapsedTime;
    }

    // 적 클립 인덱스 갱신
    foreach (var (enemyState, animState) in
        SystemAPI.Query<RefRO<EnemyState>, RefRW<VATAnimationState>>())
    {
        byte newClip = GetEnemyClipIndex(enemyState.ValueRO.CurrentState);
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

- Ghost 적(EnemySmall/EnemyFlying)은 ghost_dissolve 클립이 있으나, DisableRendering으로 인해 재생 안 될 수 있음
- Hero(LittleSquirrel)는 Dying/Dead 클립 자체가 없으므로 Idle(0)로 폴백
- 사망 연출 애니메이션이 필요하면, ClientDeathSystem 수정이 선행되어야 한다
- `math.clamp`로 바운드 보호: 클립 인덱스가 BlobArray 범위를 초과해도 안전

### 전투 기울임 시스템 (CombatTiltSystem)

**신규 파일**: `Assets/Scripts/Client/Systems/Animation/CombatTiltSystem.cs`

VAT 유무와 무관하게 **전체 유닛/적**에 적용. Attacking 상태에서 전방으로 기울이는 시각 효과.

```csharp
[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(VATAnimationPlaybackSystem))]
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
partial struct CombatTiltSystem : ISystem
```

- UnitActionState.Attacking / EnemyState.Attacking 감지 → LocalTransform.Rotation에 pitch 추가
- `quaternion.RotateX(tiltAngle)` 적용 (공격 시 앞으로 기울임)
- 상태 전환 시 lerp로 부드러운 보간 (GameSettings에 tiltAngle, tiltSpeed 추가)

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

- [x] `VATAnimationStateUpdateSystem` 구현 (서버, ISystem, VAT 적용 3종만 대상)
- [x] 유닛 클립 매핑: Hero 기준 — Idle→0, Moving→1, Working→2, 나머지→0 폴백
- [x] 적 클립 매핑: Ghost 기준 — Idle→0, Moving→1, Attacking→2, Dying→3
- [x] 변화 감지: 이전 CurrentClipIndex와 비교, 변화 시에만 갱신
- [x] VAT 미적용 유닛 자연 제외 확인 (VATAnimationState 없는 엔티티는 쿼리 미포함)
- [x] `VATAnimationInitSystem` 구현 (클라이언트, Parent 체인 탐색 패턴)
- [x] 쿼리 필터: `WithAll<MaterialMeshInfo>`, `WithNone<VATAnimParam>`
- [x] ECB Playback 타이밍 확인 (새 컴포넌트는 다음 프레임부터 유효)
- [x] `VATAnimationPlaybackSystem` 구현 (클라이언트, IJobEntity + BurstCompile)
- [x] VATAnimParam.Value 계산: normalizedTime, clipStartRow(정규화), clipRowCount(정규화)
- [x] 진행률 계산: 루프=fmod, 비루프=saturate
- [x] BlobAssetReference 접근: `clipLib.Value.Value.Clips[clipIndex]`
- [x] clipIndex 바운드 보호: `math.clamp(clipIndex, 0, Clips.Length - 1)`
- [x] 시스템 의존성 설정 (UpdateInGroup, UpdateAfter, UpdateBefore, WorldSystemFilter)
- [x] `CombatTiltSystem` 구현 (클라이언트, 전체 유닛/적 대상)
- [x] Attacking 상태 → Rotation pitch 기울임 (quaternion.RotateX)
- [x] 상태 전환 시 lerp 보간
- [x] GameSettings에 tiltAngle, tiltSpeed 필드 추가
