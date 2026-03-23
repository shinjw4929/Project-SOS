# Phase 5: 투사체 속도 단일화

**변경 파일**: 2~3개 (CombatUtility, RangedAttackSystem 또는 시각 투사체 생성 시스템)

> **핵심**: 물리 투사체(ProjectileAuthoring.speed=20f)와 시각 투사체(CombatUtility.ProjectileSpeed=30f)의 속도를 단일 소스로 통합.

---

## 현재 상태

| 위치 | 값 | 용도 |
|------|-----|------|
| `ProjectileAuthoring.speed` | 20f | 물리 투사체 이동 속도 (ProjectileMove.Speed) |
| `CombatUtility.ProjectileSpeed` | 30f | 시각 전용(VisualOnly) 투사체 속도 |

시각 투사체는 `RangedAttackSystem`에서 생성되며 `CombatUtility.ProjectileSpeed` 상수를 사용.
물리 투사체는 `ProjectileAuthoring`에서 베이킹된 `ProjectileMove.Speed` 값을 사용.

두 값이 불일치 (20 vs 30).

---

## 해결 방안

### 방안 A: CombatStats에 ProjectileSpeed 추가

원거리 공격자(RangedUnit/RangedEnemy) 프리팹에 투사체 속도를 포함:

```csharp
public struct CombatStats : IComponentData
{
    public float AttackRange;
    public float AttackSpeed;
    public float ProjectileSpeed;  // 추가
}
```

`RangedAttackSystem`에서 시각 투사체 생성 시 `combatStats.ProjectileSpeed` 사용.

**장점**: 프리팹별 투사체 속도 조정 가능
**단점**: CombatStats 컴포넌트 확장 (Ghost 동기화 필드 추가 주의)

### 방안 B: CombatUtility.ProjectileSpeed를 ProjectileAuthoring.speed 기본값으로 통일

`CombatUtility.ProjectileSpeed` 상수를 제거하고, 시각 투사체도 물리 투사체와 동일한 속도 사용.
속도 값은 공격자 엔티티에서 런타임에 읽어오거나, 고정 상수 하나로 통일.

**장점**: 단순
**단점**: 프리팹별 차별화 불가 (모든 투사체 동일 속도)

### 권장: 방안 A

원거리 유닛과 원거리 적의 투사체 속도가 다를 수 있으므로 프리팹별 설정이 바람직.

---

## 구현 상세 (방안 A)

### CombatStats 확장

**파일**: `Assets/Scripts/Shared/Components/Stats/CombatStats.cs`

```csharp
public struct CombatStats : IComponentData
{
    [GhostField] public float AttackRange;
    [GhostField] public float AttackSpeed;
    [GhostField] public float ProjectileSpeed;  // 추가 (시각 투사체 속도)
}
```

### Authoring 추가

`UnitAuthoring.cs`, `EnemyAuthoring.cs`에 `projectileSpeed` 필드 추가 (원거리 전용):
```csharp
[Header("Combat")]
public float projectileSpeed = 30f;  // 시각 투사체 속도 (원거리 전용)
```

Baker: `CombatStats.ProjectileSpeed = authoring.projectileSpeed`

### CombatUtility.ProjectileSpeed 제거

**파일**: `Assets/Scripts/Shared/Utilities/CombatUtility.cs`

```csharp
// Before:
const float ProjectileSpeed = 30f;

// 시각 투사체 생성 시:
speed = ProjectileSpeed;

// After: 상수 제거, 호출자에서 CombatStats.ProjectileSpeed 전달
```

### RangedAttackSystem 수정

시각 투사체 생성 시 `CombatStats.ProjectileSpeed` 참조:
```csharp
// Before:
float speed = CombatUtility.ProjectileSpeed;

// After:
float speed = combatStats.ProjectileSpeed;
```

---

## 체크리스트

- [ ] `CombatStats.cs`: `ProjectileSpeed` 필드 추가 + `[GhostField]`
- [ ] `UnitAuthoring.cs`: `projectileSpeed` 필드 추가 (원거리 전용), Baker 수정
- [ ] `EnemyAuthoring.cs`: 동일
- [ ] `CombatUtility.cs`: `ProjectileSpeed` 상수 제거
- [ ] `RangedAttackSystem.cs`: 시각 투사체 속도를 `CombatStats.ProjectileSpeed`로 교체
- [ ] `ProjectileAuthoring.speed` 기본값 검토 (물리 투사체 속도)
- [ ] Burst 빌드 확인
