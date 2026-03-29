# FormationFix 오케스트레이션 플랜

## 문제 정의

유닛 이동 명령 시 `HandleMoveRequestSystem.ApplyFormationOffsets`가 적용하는 격자 대형에 두 가지 결함이 있다.

1. **건물 둘레 분산 유닛 오방향 이동**: 유닛이 건물을 둘러싸고 있을 때 groupCenter가 건물 내부에 위치함. 포메이션 적용 시 일부 유닛의 목적지가 건물 반대편을 경유하는 경로를 생성하여 영구적으로 잘못된 방향으로 이동.
2. **포메이션 목적지 벽/맵 밖 설정**: 순수 기하학적 오프셋만 계산하므로 벽(IsPathBlocked) 내부나 그리드 밖에 목적지가 설정될 수 있음. 해당 유닛이 벽에 돌진하거나 맵 경계를 돌파.

**영향 범위**: `HandleMoveRequestSystem`(Server) 1개 파일. FormationUtility, FlowFieldSystem, PredictedMovementSystem은 변경 없음.

## AS-IS (현재 상태)

### 관련 파일
| 파일 | 역할 |
|------|------|
| `Server/Systems/Commands/Movement/HandleMoveRequestSystem.cs` | RPC 처리 + 포메이션 |
| `Shared/Utilities/FormationUtility.cs` | 격자 오프셋 계산 (순수 기하학) |

### 현재 동작 (ApplyFormationOffsets)
```
1. 동일 소유자 + 동일 목적지(1m 이내) 유닛을 그룹으로 묶음
2. groupCenter = 유닛 위치 평균
3. moveDir = normalize(groupDest - groupCenter)
4. 슬롯 순서대로 FormationUtility.CalculateFormationOffset 호출
5. goalRW.Destination = groupDest + offset (검증 없음)
```

### 문제 발생 조건
- **조건 1**: groupCenter가 건물(blocked cell) 위에 있음 → 유닛이 건물 양쪽에 분산 → 일부 유닛이 건물을 돌아야 하는 경로 생성
- **조건 2**: groupDest + offset 위치가 벽 내부 또는 맵 밖 → FlowFieldSystem이 보정하지만 일부 엣지 케이스에서 비정상 경로 생성

## TO-BE (목표 상태)

### 변경 내용
```
1. (기존과 동일) 그룹핑
2. (기존과 동일) groupCenter, moveDir 계산
3. [신규] groupCenter 주변에 blocked cell이 있는지 검사
   → 있으면 포메이션 건너뜀 (유닛들은 ProcessRequest에서 설정된 groupDest로 직행)
4. (기존과 동일) 슬롯별 오프셋 계산
5. [신규] 각 오프셋 목적지를 그리드 검증
   → 맵 밖이면 blocked 판정
   → IsPathBlocked이면 groupDest로 폴백
6. goalRW.Destination = 검증된 목적지
```

### 설계 원칙
- `RequireForUpdate<GridSettings>()` 추가 금지. `TryGetSingletonEntity`로 그리드 접근. 그리드 없으면 기존 동작 유지.
- 검증 로직은 `ApplyFormationOffsets` 내부에 한정. 다른 시스템 변경 없음.
- Burst 호환 유지 (DynamicBuffer, NativeArray만 사용).

## AS-IS vs TO-BE 비교표

| 항목 | AS-IS | TO-BE |
|---|---|---|
| 그리드 접근 | 없음 | TryGetSingletonEntity로 조건부 접근 |
| 건물 둘레 감지 | 없음 | groupCenter 주변 3x3 셀 IsPathBlocked 검사 |
| 건물 둘레 시 포메이션 | 적용 (오방향 유발) | 건너뜀 (groupDest 직행) |
| 목적지 벽 검증 | 없음 | IsPathBlocked + 맵 경계 검사 |
| 벽 내부 목적지 | 그대로 설정 (벽 투과) | groupDest로 폴백 |
| 맵 밖 목적지 | 그대로 설정 (맵 이탈) | blocked 판정 → groupDest 폴백 |
| RequireForUpdate | 없음 | 없음 (TryGetSingletonEntity) |

## Phase 체크리스트

### Phase 1: 포메이션 검증 로직 추가
- [x] HandleMoveRequestSystem.OnUpdate에 그리드 조건부 접근 추가
- [x] ApplyFormationOffsets에 gridCells, gridSettings 전달
- [x] groupCenter 주변 blocked cell 검사 → 포메이션 건너뜀
- [x] 각 포메이션 목적지 그리드 검증 (IsPositionBlocked)
- [x] IsPositionBlocked: 맵 밖 = blocked 반환
- [x] EditMode 테스트: IsPositionBlocked 경계 조건
→ 상세: [phase-1-formation-validation.md](./phase-1-formation-validation.md)

## Phase 간 의존성

| Phase | 의존성 | 병렬 가능 |
|---|---|---|
| 1 | 없음 | - |

## 변경 파일 요약

| Phase | 파일 | 변경 |
|---|---|---|
| 1 | `HandleMoveRequestSystem.cs` | ApplyFormationOffsets에 그리드 검증 추가 |

## 검증 방법
1. EditMode Test: IsPositionBlocked 유틸리티 테스트 (맵 밖, 벽 내부, 정상 위치)
2. PlayMode: 건물 둘레 유닛 선택 → 이동 명령 → 모든 유닛이 올바른 방향으로 이동 확인
3. PlayMode: 벽 근처에서 포메이션 → 유닛이 벽을 투과하지 않는지 확인

## 롤백 전략
- HandleMoveRequestSystem.cs 1개 파일만 변경. `git checkout -- HandleMoveRequestSystem.cs`로 즉시 롤백 가능.
