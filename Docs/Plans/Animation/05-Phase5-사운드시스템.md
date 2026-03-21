# Phase 5: 사운드 시스템 (설계 + 구조)

**전제 조건**: Phase 1
사운드 에셋 미확보 상태이므로 **시스템 설계만** 진행. 에셋 확보 후 즉시 통합 가능한 구조.

---

## 아키텍처

ECS 이벤트 → MonoBehaviour AudioSource 풀 (Hybrid 브릿지)

---

## ECS 컴포넌트

### SoundType (Shared)

**파일**: `Shared/Components/Sound/SoundType.cs`
- SoundType enum만 정의 (Shared에서 참조 가능하도록)

### SoundEvent (Client)

**파일**: `Client/Components/Sound/SoundEvent.cs`
```csharp
public struct SoundEvent : IBufferElementData
{
    public SoundType Type;     // MeleeHit, RangedShot, UnitDeath 등
    public float3 Position;
    public float Volume;
}
```

### SoundEventState (Client)

**파일**: `Client/Component/Singleton/SoundEventState.cs`
- 사운드 이벤트 싱글톤 (DynamicBuffer<SoundEvent> 보유)

---

## 시스템

### 클라이언트: SoundEventEmitSystem

**파일**: `Client/Systems/Sound/SoundEventEmitSystem.cs`
- 애니메이션 상태 변화 감지 → SoundEvent 버퍼에 추가
- 예: 공격 클립 전환 시 MeleeHit, 사망 시 Death

### MonoBehaviour: SoundManager

**파일**: `Client/Controller/Sound/SoundManager.cs`
- MinimapRenderer 패턴 참조 (World.All에서 클라이언트 월드 탐색)
- AudioSource 풀 (32개), 라운드로빈 재사용
- 카메라 거리 기반 컬링 (80m 밖 무시)
- 동일 SoundType 동시 재생 수 제한

---

## 기존 파일 수정

`ClientBootstrapSystem.cs`: SoundEventState 싱글톤 + SoundEvent 버퍼 생성 추가

---

## 체크리스트

- [ ] `SoundType` enum 정의 (Shared)
- [ ] `SoundEvent` IBufferElementData 구현 (Client)
- [ ] `SoundEventState` 싱글톤 구현 (Client)
- [ ] `SoundEventEmitSystem` 구현 (Client, 상태 변화 → 이벤트)
- [ ] `SoundManager` MonoBehaviour 구현 (AudioSource 풀, 거리 컬링)
- [ ] `ClientBootstrapSystem` 수정 — SoundEventState 싱글톤 초기화 추가
