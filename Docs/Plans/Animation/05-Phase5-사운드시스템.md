# Phase 5: 사운드 시스템 (설계 + 구조)

**전제 조건**: Phase 3 (VATAnimationInitSystem에서 PreviousClipIndex 부착)
사운드 에셋 미확보 상태이므로 **시스템 설계만** 진행. 에셋 확보 후 즉시 통합 가능한 구조.

---

## 아키텍처

ECS 이벤트 → MonoBehaviour AudioSource 풀 (Hybrid 브릿지)

```
VATAnimationPlaybackSystem (Phase 3)
    ↓ 상태 변화 감지
SoundEventEmitSystem (ECS, 클라이언트)
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
[BurstCompile]  // SoundType(byte), float3, float만 사용 → Burst 호환
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(VATAnimationPlaybackSystem))]
[UpdateBefore(typeof(TeamColorSystem))]
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
partial struct SoundEventEmitSystem : ISystem
```

#### 상태 변화 → 사운드 매핑

이전 프레임의 `CurrentClipIndex`를 추적하기 위해 `PreviousClipIndex` 컴포넌트를 사용 (Phase 1에서 정의, Phase 3 InitSystem에서 부착).

#### 전체 쿼리 구조

```csharp
// 1. 메시 엔티티 쿼리 (VATAnimTarget + PreviousClipIndex 보유)
foreach (var (target, prevClip, entity) in
    SystemAPI.Query<RefRO<VATAnimTarget>, RefRW<PreviousClipIndex>>()
        .WithEntityAccess())
{
    // 2. 루트 엔티티에서 현재 ClipIndex 읽기
    if (!animStateLookup.TryGetComponent(target.ValueRO.RootEntity, out var animState)) continue;
    byte currentClip = animState.CurrentClipIndex;
    byte previousClip = prevClip.ValueRO.Value;

    if (currentClip == previousClip) continue;  // 변화 없으면 스킵

    // 3. 유닛/적 구분 (UnitTag/EnemyTag 확인)
    bool isUnit = unitTagLookup.HasComponent(target.ValueRO.RootEntity);
    bool isEnemy = enemyTagLookup.HasComponent(target.ValueRO.RootEntity);

    // 4. ClipIndex → SoundType 매핑 (유닛/적에 따라 다른 매핑 적용)
    SoundType soundType = isUnit
        ? GetUnitSoundType(currentClip, rangedTagLookup.HasComponent(target.ValueRO.RootEntity))
        : isEnemy
            ? GetEnemySoundType(currentClip, rangedEnemyTagLookup.HasComponent(target.ValueRO.RootEntity))
            : SoundType.None;

    if (soundType == SoundType.None) { prevClip.ValueRW.Value = currentClip; continue; }

    // 5. 싱글톤 버퍼에 이벤트 추가
    var ltw = ltwLookup[target.ValueRO.RootEntity];
    soundBuffer.Add(new SoundEvent { Type = soundType, Position = ltw.Position, Volume = 1.0f });

    prevClip.ValueRW.Value = currentClip;
}
```

#### ComponentLookup 선언

```csharp
[ReadOnly] ComponentLookup<VATAnimationState> animStateLookup;
[ReadOnly] ComponentLookup<UnitTag> unitTagLookup;
[ReadOnly] ComponentLookup<EnemyTag> enemyTagLookup;
[ReadOnly] ComponentLookup<RangedUnitTag> rangedTagLookup;
[ReadOnly] ComponentLookup<RangedEnemyTag> rangedEnemyTagLookup;
[ReadOnly] ComponentLookup<LocalToWorld> ltwLookup;
```

매핑 규칙:

| 전환 조건 | SoundType |
|----------|-----------|
| 유닛: ClipIndex → 3 (Attacking) | MeleeHit (근접) 또는 RangedShot (원거리) |
| 유닛: ClipIndex → 4 (Dying) | UnitDeath |
| 유닛: ClipIndex → 2 (Working) | WorkerGather |
| 적: ClipIndex → 2 (Attacking) | MeleeHit (근접) 또는 RangedShot (원거리, EnemyFlying) |
| 적: ClipIndex → 3 (Dying) | EnemyDeath |

근접/원거리 구분: 유닛은 `RangedUnitTag`, 적은 `RangedEnemyTag` 유무로 판별.
- `RangedUnitTag`: `Shared/Components/Tags/RangedUnitTag.cs`
- `RangedEnemyTag`: `Shared/Components/Tags/RangedEnemyTag.cs`
유닛/적 구분: 루트 엔티티의 `UnitTag`/`EnemyTag` 확인 (`Shared/Components/Tags/IdentityTags.cs`에 정의).

#### GetUnitSoundType / GetEnemySoundType 구현

```csharp
static SoundType GetUnitSoundType(byte clipIndex, bool isRanged) => clipIndex switch
{
    3 => isRanged ? SoundType.RangedShot : SoundType.MeleeHit,  // Attacking
    4 => SoundType.UnitDeath,                                    // Dying
    2 => SoundType.WorkerGather,                                 // Working
    _ => SoundType.None,
};

static SoundType GetEnemySoundType(byte clipIndex, bool isRanged) => clipIndex switch
{
    2 => isRanged ? SoundType.RangedShot : SoundType.MeleeHit,  // Attacking
    3 => SoundType.EnemyDeath,                                   // Dying
    _ => SoundType.None,
};
```

> **BuildingPlace/BuildingComplete**: 이 SoundType들은 VAT 애니메이션 상태 변화에서 발생하지 않는다. 건설 시스템에서 별도로 SoundEvent를 직접 발행해야 하며, Phase 5 범위 외이다. SoundType enum에 미리 정의만 해둔다.

#### SoundEvent.Position 계산

위 전체 쿼리 구조 코드에서 `ltwLookup[target.ValueRO.RootEntity]`로 엔티티의 `LocalToWorld.Position`을 사용한다.

#### 첫 프레임 중복 이벤트 방지

VATAnimationInitSystem에서 PreviousClipIndex = 0으로 초기화하고, 초기 CurrentClipIndex도 0(Idle)이므로 첫 프레임에 불필요한 SoundEvent가 생성되지 않는다 (변화 없음). 단, 스폰 직후 즉시 이동/공격 명령이 내려진 경우 정상적으로 이벤트가 발생한다.

### MonoBehaviour: SoundManager

**파일**: `Assets/Scripts/Client/Controller/Sound/SoundManager.cs`

#### 초기화

- MinimapRenderer 패턴 참조: `World.All`에서 `WorldFlags.GameClient` 월드 탐색
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

- [ ] `SoundType` enum 정의 (Shared, byte 기반, 카테고리별 번호 대역)
- [ ] `SoundEvent` IBufferElementData 구현 (Client, `[InternalBufferCapacity(8)]`)
- [ ] `SoundEventState` 싱글톤 구현 (Client, 태그 역할)
- [ ] `PreviousClipIndex` 컴포넌트 정의 (변화 감지용, Phase 1에서 정의)
- [ ] `SoundEventEmitSystem` 구현 (Client, `[BurstCompile]` 적용)
- [ ] 상태 변화 감지: PreviousClipIndex vs CurrentClipIndex 비교
- [ ] SoundEvent.Position: `LocalToWorld.Position` 사용
- [ ] 첫 프레임 중복 이벤트 방지 확인 (PreviousClipIndex=0, CurrentClipIndex=0)
- [ ] 클립 전환 → SoundType 매핑 규칙 구현 (유닛/적 구분: UnitTag/EnemyTag + 근접/원거리 구분: RangedUnitTag/RangedEnemyTag)
- [ ] `SoundManager` MonoBehaviour 구현
- [ ] AudioSource 풀 (32개, 라운드로빈)
- [ ] 3D AudioSource 설정 (spatialBlend=1, minDistance=5, maxDistance=80)
- [ ] 카메라 거리 컬링 (80m, 컬링 후 카운트 증가)
- [ ] 동일 SoundType 동시 재생 제한 (최대 3개)
- [ ] 버퍼 Clear 타이밍: SoundManager.Update() 마지막에 수행
- [ ] OnDestroy에서 AudioSource 풀 정리
- [ ] `ClientBootstrapSystem` 수정 — SoundEventState 싱글톤 + SoundEvent 버퍼 생성
