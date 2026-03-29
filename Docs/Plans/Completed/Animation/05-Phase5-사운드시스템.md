# Phase 5: 사운드 시스템 (설계 + 구조)

**전제 조건**: Phase 1 (SoundType enum)
사운드 에셋 미확보 상태이므로 **시스템 설계만** 진행. 에셋 확보 후 즉시 통합 가능한 구조.

> **설계 변경**: 원래 VAT 클립 인덱스(PreviousClipIndex) 기반으로 사운드 이벤트를 감지했으나, VAT 미적용 유닛 5종(Origami 4종 + EnemyBig)에서 사운드가 발생하지 않는 문제가 있었음. **UnitActionState/EnemyState를 직접 감지**하는 방식으로 변경하여 VAT 유무와 무관하게 전체 유닛/적에서 사운드 이벤트 발생.

---

## 아키텍처

ECS 이벤트 → MonoBehaviour AudioSource 풀 (Hybrid 브릿지)

```
UnitActionState / EnemyState 변화 (서버 → Ghost 동기화)
    ↓ 클라이언트에서 직접 감지 (VAT 유무 무관)
SoundEventEmitSystem (ECS, 클라이언트, PreviousActionState/PreviousEnemyContext 비교)
    ↓ SoundEvent 버퍼에 이벤트 추가
SoundManager (MonoBehaviour)
    ↓ 매 프레임 버퍼 소비
AudioSource 풀 (32개)
    ↓ 카메라 거리 컬링, 동시 재생 제한
실제 사운드 출력
```

---

## ECS 컴포넌트

### SoundType (Shared)

**파일**: `Assets/Scripts/Shared/Components/Sound/SoundType.cs`

```csharp
public enum SoundType : byte
{
    None = 0,

    // 전투
    MeleeHit = 10,       // 근접 공격 명중
    RangedShot = 11,     // 원거리 공격 발사
    UnitDeath = 12,      // 유닛 사망
    EnemyDeath = 13,     // 적 사망

    // 작업
    WorkerGather = 20,   // 채집
    BuildingPlace = 21,  // 건물 배치
    BuildingComplete = 22, // 건설 완료

    // 이동 (향후 확장용 — VATAnimationState 변화가 아닌 별도 트리거 필요)
    MoveCommand = 30,    // 이동 명령 (Phase 5 범위 외, 입력 시스템에서 직접 발생 예정)
}
```

### SoundEvent (Client)

**파일**: `Assets/Scripts/Client/Component/Sound/SoundEvent.cs`

```csharp
public struct SoundEvent : IBufferElementData
{
    public SoundType Type;
    public float3 Position;
    public float Volume;     // 0~1, 기본값 1.0
}
```

### SoundEventState (Client)

**파일**: `Assets/Scripts/Client/Component/Singleton/SoundEventState.cs`

```csharp
public struct SoundEventState : IComponentData { }
// SoundEventState는 싱글톤 버퍼 엔티티의 마커 컴포넌트 (데이터 없음)
// 역할: SystemAPI.GetSingletonEntity<SoundEventState>()로 버퍼 엔티티를 찾기 위한 식별자
// 실제 사운드 이벤트 데이터는 DynamicBuffer<SoundEvent>가 담당 (같은 엔티티에 부착)
// ClientBootstrapSystem에서 싱글톤 엔티티 생성 시 함께 추가
```

### SoundEvent 버퍼 용량

```csharp
[InternalBufferCapacity(8)]  // 대규모 전투에서도 한 프레임에 수십 개 이상 누적 안 됨
public struct SoundEvent : IBufferElementData { ... }
```

---

## 시스템

### 클라이언트: SoundEventEmitSystem

**파일**: `Assets/Scripts/Client/Systems/Sound/SoundEventEmitSystem.cs`

```csharp
[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(VATAnimationPlaybackSystem))]
[UpdateBefore(typeof(TeamColorSystem))]
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
partial struct SoundEventEmitSystem : ISystem
```

#### 설계 변경: VAT 독립 방식

~~원래 VATAnimTarget + PreviousClipIndex 기반~~ → **UnitActionState/EnemyState 직접 감지** 방식으로 변경.

이유: VAT 미적용 유닛 5종(Origami Worker/Striker/Tank/Archer + EnemyBig)은 VATAnimTarget이 없어 사운드 이벤트가 발생하지 않는 문제. ActionState/EnemyState는 모든 유닛/적에 존재하므로 VAT 유무와 무관하게 동작.

#### 추가 컴포넌트

```csharp
// 유닛 상태 변화 감지용 (Client)
// 파일: Assets/Scripts/Client/Component/State/PreviousActionState.cs
public struct PreviousActionState : IComponentData
{
    public Action Value;
}

// 적 상태 변화 감지용 (Client)
// 파일: Assets/Scripts/Client/Component/State/PreviousEnemyContext.cs
public struct PreviousEnemyContext : IComponentData
{
    public EnemyContext Value;
}
```

이 컴포넌트들은 ClientBootstrapSystem 또는 SoundEventEmitSystem 내에서 `WithNone` 쿼리 + ECB로 초기 부착.

#### 전체 쿼리 구조

```csharp
// 1. 유닛 사운드 이벤트
foreach (var (actionState, prevAction, ltw, entity) in
    SystemAPI.Query<RefRO<UnitActionState>, RefRW<PreviousActionState>, RefRO<LocalToWorld>>()
        .WithEntityAccess())
{
    var current = actionState.ValueRO.State;
    var previous = prevAction.ValueRO.Value;

    if (current == previous) continue;

    bool isRanged = rangedTagLookup.HasComponent(entity);
    SoundType soundType = GetUnitSoundType(current, isRanged);

    if (soundType != SoundType.None)
        soundBuffer.Add(new SoundEvent { Type = soundType, Position = ltw.ValueRO.Position, Volume = 1.0f });

    prevAction.ValueRW.Value = current;
}

// 2. 적 사운드 이벤트
foreach (var (enemyState, prevCtx, ltw, entity) in
    SystemAPI.Query<RefRO<EnemyState>, RefRW<PreviousEnemyContext>, RefRO<LocalToWorld>>()
        .WithEntityAccess())
{
    var current = enemyState.ValueRO.CurrentState;
    var previous = prevCtx.ValueRO.Value;

    if (current == previous) continue;

    bool isRanged = rangedEnemyTagLookup.HasComponent(entity);
    SoundType soundType = GetEnemySoundType(current, isRanged);

    if (soundType != SoundType.None)
        soundBuffer.Add(new SoundEvent { Type = soundType, Position = ltw.ValueRO.Position, Volume = 1.0f });

    prevCtx.ValueRW.Value = current;
}
```

#### ComponentLookup 선언

```csharp
[ReadOnly] ComponentLookup<RangedUnitTag> rangedTagLookup;
[ReadOnly] ComponentLookup<RangedEnemyTag> rangedEnemyTagLookup;
```

> VATAnimationState, VATAnimTarget, UnitTag/EnemyTag Lookup은 불필요 — 쿼리 자체가 UnitActionState/EnemyState로 유닛/적을 구분.

매핑 규칙:

| 전환 조건 | SoundType |
|----------|-----------|
| 유닛: Action → Attacking | MeleeHit (근접) 또는 RangedShot (원거리) |
| 유닛: Action → Dying | UnitDeath |
| 유닛: Action → Working | WorkerGather |
| 적: EnemyContext → Attacking | MeleeHit (근접) 또는 RangedShot (원거리) |
| 적: EnemyContext → Dying | EnemyDeath |

근접/원거리 구분: 유닛은 `RangedUnitTag`, 적은 `RangedEnemyTag` 유무로 판별.

#### GetUnitSoundType / GetEnemySoundType 구현

```csharp
static SoundType GetUnitSoundType(Action action, bool isRanged) => action switch
{
    Action.Attacking => isRanged ? SoundType.RangedShot : SoundType.MeleeHit,
    Action.Dying     => SoundType.UnitDeath,
    Action.Working   => SoundType.WorkerGather,
    _                => SoundType.None,
};

static SoundType GetEnemySoundType(EnemyContext ctx, bool isRanged) => ctx switch
{
    EnemyContext.Attacking => isRanged ? SoundType.RangedShot : SoundType.MeleeHit,
    EnemyContext.Dying     => SoundType.EnemyDeath,
    _                      => SoundType.None,
};
```

> **BuildingPlace/BuildingComplete**: 건설 시스템에서 별도로 SoundEvent를 직접 발행해야 하며, Phase 5 범위 외. SoundType enum에 미리 정의만 해둔다.

#### SoundEvent.Position 계산

쿼리에서 `RefRO<LocalToWorld>`를 직접 읽으므로 별도 Lookup 불필요.

#### 첫 프레임 중복 이벤트 방지

PreviousActionState는 `Action.Idle`(0)으로 초기화, UnitActionState 초기값도 Idle이므로 첫 프레임에 불필요한 SoundEvent 없음. PreviousEnemyContext도 동일.

### MonoBehaviour: SoundManager

**파일**: `Assets/Scripts/Client/Controller/Sound/SoundManager.cs`

#### 초기화

- MinimapRenderer 패턴 참조: `World.All`에서 `world.IsClient()` 확인으로 클라이언트 월드 탐색
- `SystemAPI.TryGetSingletonBuffer<SoundEvent>` 접근 불가 (MonoBehaviour) → `EntityManager.GetBuffer<SoundEvent>` 사용
- SoundEventState 싱글톤 엔티티의 DynamicBuffer<SoundEvent> 직접 읽기

#### AudioSource 풀

- 풀 크기: 32개 AudioSource (SoundType 8종 x 동시 3개 = 24개 최대, 8개 여유)
- 재사용: 라운드로빈 (인덱스 순환)
- 재생 중인 AudioSource를 덮어쓸 때: 가장 오래된 것부터 재사용 (자연 만료)
- OnDestroy에서 모든 AudioSource.Stop() 호출 (풀 정리)

#### 3D AudioSource 설정

```csharp
// 각 AudioSource 초기화 시 설정
audioSource.spatialBlend = 1.0f;     // 완전 3D
audioSource.minDistance = 5f;         // 최소 거리 (감쇠 시작)
audioSource.maxDistance = 80f;        // 최대 거리 (컬링 거리와 일치)
audioSource.rolloffMode = AudioRolloffMode.Linear;
```

#### 사운드 컬링

- 카메라 거리 기반: `Camera.main.transform.position`과 이벤트 Position 간 거리 > **80m** → 무시
- 동일 SoundType 동시 재생 제한: **최대 3개** (초과 시 무시)
- 컬링 순서: 거리 컬링 → 동시 재생 수 확인 → 재생 (컬링 후 카운트 증가)

#### Update 루프

```
1. 클라이언트 월드에서 SoundEvent 버퍼 읽기
2. 각 이벤트 순회:
   a. 카메라 거리 컬링 (80m)
   b. 동일 타입 동시 재생 수 확인 (3개 제한, 컬링 후 카운트)
   c. SoundType → AudioClip 매핑 (Inspector 할당)
   d. 풀에서 AudioSource 할당, position 설정, PlayOneShot
3. 버퍼 Clear (Update 마지막에 entityManager.GetBuffer<SoundEvent>(entity).Clear())
```

버퍼 Clear는 SoundManager.Update() 마지막에 수행한다. SoundEventEmitSystem과 SoundManager는 같은 프레임에 실행되므로, SoundManager가 버퍼를 소비한 후 즉시 Clear하여 다음 프레임에 이벤트가 누적되지 않도록 한다.

---

## 기존 파일 수정

### ClientBootstrapSystem.cs

기존 싱글톤 생성 패턴에 SoundEventState 추가:

```csharp
// SoundEventState 싱글톤 + SoundEvent 버퍼 생성
if (!SystemAPI.HasSingleton<SoundEventState>())
{
    var entity = entityManager.CreateEntity(typeof(SoundEventState));
    entityManager.AddBuffer<SoundEvent>(entity);

#if UNITY_EDITOR
    entityManager.SetName(entity, "Singleton_SoundEventState");
#endif
}
```

---

## 체크리스트

- [x] `SoundType` enum 정의 (Shared, byte 기반, 카테고리별 번호 대역)
- [x] `SoundEvent` IBufferElementData 구현 (Client, `[InternalBufferCapacity(8)]`)
- [x] `SoundEventState` 싱글톤 구현 (Client, 태그 역할)
- [x] `PreviousActionState` 컴포넌트 정의 (유닛 상태 변화 감지용, Client)
- [x] `PreviousEnemyContext` 컴포넌트 정의 (적 상태 변화 감지용, Client)
- [x] `SoundEventEmitSystem` 구현 (Client, `[BurstCompile]` 적용)
- [x] 상태 변화 감지: UnitActionState/EnemyState 직접 비교 (VAT 독립)
- [x] SoundEvent.Position: `LocalToWorld.Position` 사용 (쿼리에서 직접 읽기)
- [x] 첫 프레임 중복 이벤트 방지 확인 (PreviousActionState=Idle, CurrentAction=Idle)
- [x] Action/EnemyContext → SoundType 매핑 규칙 구현 (근접/원거리 구분: RangedUnitTag/RangedEnemyTag)
- [x] `SoundManager` MonoBehaviour 구현
- [x] AudioSource 풀 (32개, 라운드로빈)
- [x] 3D AudioSource 설정 (spatialBlend=1, minDistance=5, maxDistance=80)
- [x] 카메라 거리 컬링 (80m, 컬링 후 카운트 증가)
- [x] 동일 SoundType 동시 재생 제한 (최대 3개)
- [x] 버퍼 Clear 타이밍: SoundManager.Update() 마지막에 수행
- [x] OnDestroy에서 AudioSource 풀 정리
- [x] `ClientBootstrapSystem` 수정 — SoundEventState 싱글톤 + SoundEvent 버퍼 생성
