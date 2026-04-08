# Phase 1: CarryHeightOffset 컴포넌트 추가 및 시스템 수정

## 목표
- 자원 운반 높이를 유닛별로 설정 가능하게 하여 Worker/Hero 등 키가 다른 유닛 대응

## 선행 조건
- 없음

## 작업 목록

### Task 1: CarryHeightOffset 컴포넌트 생성
- [ ] `Assets/Scripts/Shared/Components/Stats/CarryHeightOffset.cs` 생성
- [ ] `namespace Shared`, `IComponentData`, `float Value` 필드 (`GatheringAbility`와 동일 패턴 - Ghost 동기화 없는 Worker 전용)
- [ ] Ghost 동기화 불필요 (프리팹 베이킹 상수)

### Task 2: UnitAuthoring에 필드 추가 및 베이킹
- [ ] `UnitAuthoring.cs`의 기존 `[Header("Gathering Settings (Worker Only)")]` 섹션에 `[Tooltip("운반 자원의 머리 위 높이 오프셋")] public float carryHeightOffset = 1.2f;` 필드 추가 (별도 헤더 생성 불필요)
- [ ] `isWorker` 분기 내 (`if (isWorker)` 블록, 기존 `GatheringAbility`/`WorkerState` 추가 위치 근처)에 베이킹 코드 추가:
  ```csharp
  AddComponent(entity, new CarryHeightOffset
  {
      Value = authoring.carryHeightOffset
  });
  ```

### Task 3: CarriedResourceFollowSystem 수정
- [ ] `_workerStateLookup` 옆에 `ComponentLookup<CarryHeightOffset> _carryHeightLookup` 추가
- [ ] `OnCreate`에서 `state.GetComponentLookup<CarryHeightOffset>(true)` 초기화
- [ ] `OnUpdate`에서 `_carryHeightLookup.Update(ref state)` 호출
- [ ] 라인 53의 `new float3(0, 1.2f, 0)` → `CarryHeightOffset` Lookup으로 교체:
  ```csharp
  // 정상 경로에서는 모든 Worker/Hero에 CarryHeightOffset이 베이킹됨. fallback은 방어적 처리.
  float heightOffset = _carryHeightLookup.TryGetComponent(workerEntity, out var carryHeight)
      ? carryHeight.Value
      : 1.2f;
  transform.ValueRW.Position = workerPos + new float3(0, heightOffset, 0);
  ```
- [ ] 주석 업데이트: 53번 줄 인라인 주석 + 12번 줄 클래스 XML summary의 "위치: Worker 머리 위 (Y + 1.2f)" → "위치: Worker 머리 위 (CarryHeightOffset)"

### Task 4: 프리팹 인스펙터 설정 (Unity Editor 수동 작업)
- [ ] Worker 프리팹: `carryHeightOffset = 1.2f`
- [ ] Hero 프리팹: `carryHeightOffset = 1.8f` (Hero 높이 2.0에 맞춰 조정, 플레이 테스트 후 미세 조정)

## 병렬 작업 구성 (subagent 활용)

> Task 1~3은 모두 같은 파일 또는 의존 관계이므로 순차 실행.

| Agent | 작업 내용 | 의존성 |
|---|---|---|
| Main | Task 1 → Task 2 → Task 3 순차 실행 | 없음 |
| User | Task 4 (Unity Editor 수동 작업) | Task 1~3 완료 후 |

## 테스트 요구사항

### EditMode Test
- 이 변경은 ECS 시스템 + Lookup 조합이므로 순수 함수 테스트 대상 없음

### PlayMode Test (선택)
- CarriedResourceFollowSystem이 CarryHeightOffset을 올바르게 읽는지 통합 테스트
- 단, 기존에 이 시스템의 테스트가 없으므로 필수는 아님

## 검증 방법
1. 컴파일 성공
2. Worker 프리팹의 `CarryHeightOffset` 인스펙터 값 = 1.2f 확인
3. Hero 프리팹의 `CarryHeightOffset` 인스펙터 값 = 1.8f 확인
4. 플레이 테스트: Worker/Hero 각각 cheese 운반 시 머리 위 적절한 높이에 표시

### Task 5: 문서 업데이트
- [ ] `Docs/Systems/자원 채집 시스템.md` 69번 줄 설명 + 209번 줄 코드 예시 모두 `CarryHeightOffset` 기반으로 갱신
- [ ] `Docs/Systems/코드베이스 구조.md` Stats 컴포넌트 목록에 `CarryHeightOffset.cs` 추가

## 완료 기준
- [ ] 컴파일 성공
- [ ] Worker/Hero 프리팹에 `carryHeightOffset` 값 설정됨
- [ ] `Docs/Systems/자원 채집 시스템.md` 문서 업데이트 완료
- [ ] 플레이 테스트에서 유닛별 다른 높이에 cheese 표시 확인
