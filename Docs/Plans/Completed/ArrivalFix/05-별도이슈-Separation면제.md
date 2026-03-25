# 별도 이슈: 건설/채집 유닛 Separation 면제 확대

## 기획 의도

건설(Intent.Build) 및 채집(Intent.Gather) 중인 유닛은 다른 유닛에 의해 밀리지 않아야 한다.

## 현재 상태

**파일**: `PredictedMovementSystem.cs:196-198, 277-281`

```csharp
// line 196-198: 자기 자신 채집 여부 확인
bool iAmGathering = false;
if (IntentLookup.TryGetComponent(entity, out UnitIntentState intent) && intent.State == Intent.Gather)
    iAmGathering = true;

// line 277-279: 이웃 채집 여부 확인
bool isGathering = false;
if (IntentLookup.TryGetComponent(neighbor.Entity, out UnitIntentState nIntent) && nIntent.State == Intent.Gather)
    isGathering = true;

// line 281: 충돌 판정
bool shouldCollide = iAmEnemy || isEnemy || (!iAmGathering && !isGathering);
```

### 면제 매트릭스 (현재)

|  | 채집 워커 | 건설 워커 | 일반 유닛 | 적 |
|--|----------|----------|----------|-----|
| **채집 워커** | 면제 | 적용 | 적용 | 적용 |
| **건설 워커** | 적용 | 적용 | 적용 | 적용 |
| **일반 유닛** | 적용 | 적용 | 적용 | 적용 |
| **적** | 적용 | 적용 | 적용 | 적용 |

### 문제점

1. **건설 워커 면제 누락**: `Intent.Build` 체크가 없어 건설 중 유닛이 밀림
2. **채집 워커 부분 면제**: 채집 워커끼리만 면제, 일반 유닛/적에게는 밀림
3. **작업 중(Working) 상태에서도 밀림**: MovementWaypoints 비활성화 시에도 Separation이 적용됨 (line 193: "공격 중에도 실행")

## 수정 방향

### 면제 매트릭스 (수정 후)

|  | 작업 워커 | 일반 유닛 | 적 |
|--|----------|----------|-----|
| **작업 워커** | 면제 | 면제 | 적용 |
| **일반 유닛** | 면제 | 적용 | 적용 |
| **적** | 적용 | 적용 | 적용 |

> "작업 워커" = Intent.Gather 또는 Intent.Build인 유닛

### 코드 변경

**PredictedMovementSystem.cs**

```csharp
// line 196-198 변경:
bool iAmWorking = false;
if (IntentLookup.TryGetComponent(entity, out UnitIntentState intent)
    && (intent.State == Intent.Gather || intent.State == Intent.Build))
    iAmWorking = true;

// line 200: 파라미터명 변경
float3 separationForce = CalculateSeparation(currentPos, obstacleRadius.Radius, entity, iAmEnemy, iAmFlying, iAmWorking);

// line 277-279 변경:
bool isWorking = false;
if (IntentLookup.TryGetComponent(neighbor.Entity, out UnitIntentState nIntent)
    && (nIntent.State == Intent.Gather || nIntent.State == Intent.Build))
    isWorking = true;

// line 281 변경:
bool shouldCollide = iAmEnemy || isEnemy || (!iAmWorking && !isWorking);
```

**CalculateSeparation 시그니처** (line 253-255):

```csharp
// 기존:
private float3 CalculateSeparation(
    float3 myPos, float myRadius, Entity myEntity,
    bool iAmEnemy, bool iAmFlying, bool iAmGathering)

// 변경:
private float3 CalculateSeparation(
    float3 myPos, float myRadius, Entity myEntity,
    bool iAmEnemy, bool iAmFlying, bool iAmWorking)
```

### 면제 조건 변경점

```
// 기존: 양쪽 모두 채집 중이어야 면제
!iAmGathering && !isGathering  →  둘 다 false여야 충돌

// 변경: 한쪽이라도 작업 중이면 면제 (적 제외)
!iAmWorking && !isWorking      →  둘 다 false여야 충돌
```

적(Enemy)은 `iAmEnemy || isEnemy` 조건이 먼저 평가되므로, 적과 작업 워커 간에는 항상 Separation 적용.

## 영향 범위

- **PredictedMovementSystem.cs**: CalculateSeparation 내부 로직만 변경
- **다른 시스템 영향 없음**: Separation은 이 시스템에서만 계산
- **도착 판정 수정과 독립**: 이번 변경은 이동 오차를 줄이지만, +CellSize 마진은 Separation이 존재하는 상태에서도 동작하도록 설계됨

## 우선순위

도착 판정 수정(ArrivalFix) 이후 별도 작업. ArrivalFix는 현재 Separation이 적용되는 상태를 전제로 설계되었으므로 순서 무관.
