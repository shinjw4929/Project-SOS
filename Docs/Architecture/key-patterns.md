# Key Patterns

## 1. DamageEvent Buffer Pattern (필수)
Health를 여러 시스템에서 직접 수정하면 Job 스케줄링 충돌이 발생한다. **DamageEvent 버퍼**를 사용한다.
```csharp
// ❌ 잘못된 방법: Health 직접 수정 → Job 충돌!
var health = _healthLookup[targetEntity];
health.CurrentValue -= damage;
_healthLookup[targetEntity] = health;

// ✅ 올바른 방법: DamageEvent 버퍼에 추가
if (_damageEventLookup.HasBuffer(targetEntity))
{
    var buffer = _damageEventLookup[targetEntity];
    buffer.Add(new DamageEvent { Damage = finalDamage });
}
// DamageApplySystem이 나중에 버퍼를 읽어서 Health에 적용
```

## 2. Authoring Composition Pattern
| 프리팹 | Authoring 조합 |
|--------|----------------|
| 유닛 (Hero, Worker 등) | `MovementAuthoring` + `UnitMovementAuthoring` + `UnitAuthoring` (+ `VATAnimationAuthoring` for Hero) |
| 적 (Enemy) | `MovementAuthoring` + `EnemyAuthoring` (+ `VATAnimationAuthoring` for EnemySmall/EnemyFlying) |
| 건물 (Wall, Barracks 등) | `StructureAuthoring` |

## 3. User State Machine
```csharp
public enum UserContext : byte {
    Command = 0,              // 기본 명령 상태
    BuildMenu = 1,            // 건설 메뉴 (빌더 Q)
    Construction = 2,         // 건물 배치 모드
    StructureActionMenu = 10, // 생산 메뉴 (건물 Q)
    Dead = 255,               // 사망/게임오버
}
```

## 4. Work Range Pattern (작업 거리 계산)
모든 작업(채집, 건설, 전투)의 상호작용 거리는 **타겟 표면 기준**으로 계산한다. 공통 로직은 `ArrivalUtility`(`Shared/Utilities/ArrivalUtility.cs`)에 집약되어 있다.
```csharp
// 채집/건설: 도착 거리 = 타겟 반지름 + WorkRange (ArrivalUtility.GetInteractionArrivalDistance)
float arrivalDistance = ArrivalUtility.GetInteractionArrivalDistance(targetRadius, workRange);

// 접근점 계산: 타겟 표면까지의 이동 목표 (ArrivalUtility.CalculateApproachPoint)
float3 approachPos = ArrivalUtility.CalculateApproachPoint(fromPos, targetPos, targetEntity, in radiusLookup);

// Dead Zone 방지: ArrivalRadius 설정 (ArrivalUtility.GetSafeArrivalRadius)
float arrivalRadius = ArrivalUtility.GetSafeArrivalRadius(workRange);

// 전투: 유효 거리 = 직선 거리 - 타겟 반지름 (CombatUtility)
float effectiveDistance = rawDistance - targetRadius;
bool inRange = effectiveDistance <= attackRange;
```
- **공격자/작업자의 반지름 사용 안 함**: 타겟 표면까지의 거리만 계산
- **WorkRange/AttackRange**: 프리팹 인스펙터에서 조정 가능 (UnitAuthoring.workRange)
- **일관성**: 채집/건설 시스템은 `ArrivalUtility`를 공유, 전투는 `CombatUtility` 사용

## 5. Other Patterns (간략)
- **Selection System**: Phase 기반 (`UserSelectionInputState.Phase`) → `EntitySelectionSystem`에서 Selected 토글
- **Combat Flow**: MeleeAttackSystem/RangedAttackSystem → DamageEvent 버퍼 → DamageApplySystem
- **CarriedResource Visibility**: Scale 토글 (`CarriedAmount > 0 ? 1f : 0f`) - Structural Change 없음
- **Spatial Partitioning**: `SpatialMapBuildSystem`에서 Persistent 맵 Clear + 재빌드 → 사용 시스템에서 ReadOnly → Job dependency chain으로 동기화
- **Catalog Patterns**: UnitCatalog/StructureCatalog(버퍼) vs EnemyPrefabCatalog(명시적 필드)

## 6. VAT Animation Pattern
GPU Animation (Vertex Animation Texture) 방식으로 수천 유닛을 동시 애니메이팅. Animator/SkinnedMeshRenderer 없이 MeshRenderer + 커스텀 셰이더만 사용.
- **베이킹**: 에디터 툴(`VATBakerWindow`)로 스켈레탈 애니메이션 → Position Texture(RGBAHalf) + Static Mesh(UV2 버텍스 인덱스) + VATClipDataAsset 생성
- **서버**: `VATAnimationStateUpdateSystem`이 UnitActionState/EnemyState → `VATAnimationState.CurrentClipIndex` 갱신 (Ghost 동기화)
- **클라이언트**: `VATAnimationPlaybackSystem`이 `VATAnimParam`(MaterialProperty float4) 계산 → 셰이더가 텍스처 룩업으로 버텍스 변형
- **사운드**: `SoundEventEmitSystem`이 상태 변화 감지 + `CombatStats.AttackSpeed` 타이머로 공격 반복 재생 + 자기 유닛 스폰 감지(`GhostOwnerIsLocal` 필터) → `SoundEvent` 버퍼 → `SoundManager`(MonoBehaviour)가 AudioSource 풀로 재생 (타입별 볼륨 조절). Ghost 재생성 시 이미 Attacking인 엔티티는 타이머를 공격 간격으로 초기화하여 즉시 발동 방지.
- **엔티티 기울임**: `EntityTiltSystem`이 `PostTransformMatrix`를 사용하여 Ghost 동기화와 독립적으로 pitch 기울임 적용. Attacking: `CombatTiltTimer` 기반 half-sine swing-return 사이클 (`CombatStats.AttackSpeed`와 동기화). Dying: 점진적 전방 기울임 (`DeathTiltAngle`까지). VAT 유무 무관, 전체 유닛/적 대상.
- **대상**: VAT 적용(Hero, EnemySmall, EnemyFlying) + VAT 미적용(Worker/Striker/Tank/Archer/EnemyBig은 기존 정적 메시 유지, 기울임+사운드만)
