# Phase 4: Enemy 감지/추적 거리 통합

**변경 파일**: 4개 (UnifiedTargetingSystem, AggroReactionSystem, EnemyAuthoring, EnemyChaseDistance)

> **핵심**: `EnemyChaseDistance` 컴포넌트 제거. 이탈 거리를 `VisionRange × HysteresisMultiplier(1.3)` 로 파생.

---

## 현재 상태

| 필드 | 기본값 | 용도 |
|------|--------|------|
| `VisionRange.Value` | 10.0f | 적 탐지 범위 (SpatialMap 탐색 반경) |
| `EnemyChaseDistance.LoseTargetDistance` | 15.0f (Inspector: `aggroRange`) | 타겟 이탈 거리 |
| `HysteresisMultiplier` | 1.3f | 이탈 판정 배수 (고착화 방지) |

현재 이탈 거리: `LoseTargetDistance × HysteresisMultiplier = 15 × 1.3 = 19.5`

## 변경 후

이탈 거리: `VisionRange × HysteresisMultiplier = VisionRange × 1.3`

기본값 `visionRange=10` → 이탈거리 `13.0` (기존 19.5보다 줄어듦). **프리팹의 visionRange 값 조정 필요.**

---

## EnemyChaseDistance 참조 전수

### 1. UnifiedTargetingSystem.cs (값 참조)

**파일**: `Assets/Scripts/Server/Systems/Combat/UnifiedTargetingSystem.cs`

```csharp
// Before (line 326):
int searchRadius = (int)math.ceil(chaseDistance.ValueRO.LoseTargetDistance / CellSize);

// After:
int searchRadius = (int)math.ceil(visionRange.ValueRO.Value * HysteresisMultiplier / CellSize);
```

EnemyTargetJob Execute 파라미터에서 `in EnemyChaseDistance chaseDistance` 제거.
현재 EnemyTargetJob에는 `VisionRange` 파라미터가 **없으므로** `RefRO<VisionRange> visionRange` 추가 필요.

이탈 판정도 동일하게 교체:
```csharp
// Before:
float loseDistSq = chaseDistance.ValueRO.LoseTargetDistance * HysteresisMultiplier;
loseDistSq *= loseDistSq;

// After:
float loseDist = visionRange.ValueRO.Value * HysteresisMultiplier;
float loseDistSq = loseDist * loseDist;
```

### 2. AggroReactionSystem.cs (쿼리 필터만)

**파일**: `Assets/Scripts/Server/Systems/Combat/AggroReactionSystem.cs`

```csharp
// Before (line 87):
private void Execute(
    Entity entity,
    ref AggroTarget aggroTarget,
    ref AggroLock aggroLock,
    ref DynamicBuffer<DamageEvent> damageEvents,
    in EnemyChaseDistance chaseDistance,  // ← 값 미참조, 쿼리 필터 역할
    in Team myTeam)

// After:
private void Execute(
    Entity entity,
    ref AggroTarget aggroTarget,
    ref AggroLock aggroLock,
    ref DynamicBuffer<DamageEvent> damageEvents,
    in Team myTeam)
```

`[WithAll(typeof(EnemyTag))]`로 이미 적 전용 필터링. `EnemyChaseDistance` 파라미터 제거 안전.

### 3. EnemyAuthoring.cs (Baker 매핑)

**파일**: `Assets/Scripts/Authoring/Entities/EnemyAuthoring.cs`

```csharp
// Before:
[Header("Aggro")]
[Min(1f)] public float aggroRange = 15.0f;

// Baker:
AddComponent(entity, new EnemyChaseDistance { LoseTargetDistance = authoring.aggroRange });

// After: aggroRange 필드 + Baker 매핑 제거
// visionRange Tooltip 보강:
[Header("Vision")]
[Tooltip("탐지 범위 + 추적 이탈 거리 (×1.3) 겸용")]
public float visionRange = 10.0f;
```

### 4. EnemyChaseDistance.cs (컴포넌트 파일)

**파일**: `Assets/Scripts/Shared/Components/Data/EnemyChaseDistance.cs`

파일 삭제.

---

## 밸런스 조정

기존 동작 유지를 원하면 프리팹 `visionRange` 값 조정 필요:

| 프리팹 | 기존 aggroRange | 기존 이탈거리 | 신규 visionRange | 신규 이탈거리 |
|--------|----------------|-------------|-----------------|-------------|
| 기본 적 | 15 | 19.5 | **15** | 19.5 |

`visionRange`을 기존 `aggroRange` 값(15)으로 상향하면 이탈 거리가 동일하게 유지됨.
단, 탐지 범위도 10→15로 확대되므로 게임플레이 영향 확인 필요.

---

## 체크리스트

- [ ] `UnifiedTargetingSystem.cs` EnemyTargetJob.Execute(): `RefRO<VisionRange> visionRange` 파라미터 추가 (현재 없음, UnitAutoTargetJob에는 이미 존재)
- [ ] `UnifiedTargetingSystem.cs` EnemyTargetJob: `in EnemyChaseDistance chaseDistance` 파라미터 → `RefRO<VisionRange> visionRange`로 교체
- [ ] `UnifiedTargetingSystem.cs` EnemyTargetJob: `chaseDistance.ValueRO.LoseTargetDistance` → `visionRange.ValueRO.Value * HysteresisMultiplier` 교체 (line 156, 326)
- [ ] `AggroReactionSystem.cs` EnemyAggroReactionJob: `in EnemyChaseDistance` 파라미터 제거
- [ ] `EnemyAuthoring.cs`: `aggroRange` 필드 + Baker 제거, `visionRange` Tooltip 보강
- [ ] `EnemyChaseDistance.cs`: 파일 삭제
- [ ] 프리팹 `visionRange` 값 조정 (사용자 결정)
- [ ] Burst 빌드 확인
