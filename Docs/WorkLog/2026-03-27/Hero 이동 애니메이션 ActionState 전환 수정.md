# Hero 이동 애니메이션 ActionState 전환 수정

**날짜**: 2026-03-27
**작업**: Hero 이동 명령 시 UnitActionState가 Moving으로 전환되지 않아 Walk 애니메이션이 재생되지 않던 문제 수정

---

## 변경 파일 목록

### 수정 파일

| 파일 | 변경 내용 |
|------|-----------|
| `Assets/Scripts/Server/Systems/Commands/Movement/HandleMoveRequestSystem.cs` | `UnitActionState` Lookup 추가, 이동 명령 수신 시 `Action.Moving` 설정 |
| `Assets/Scripts/Server/Systems/Movement/MovementArrivalSystem.cs` | 쿼리에 `UnitActionState` 추가, `Intent.Move` 도착 시 `Action.Idle` 설정 |

### 문서 수정

| 파일 | 변경 내용 |
|------|-----------|
| `Docs/Plans/Animation/08-미해결이슈.md` | 이슈 #4 상태를 "완료"로 변경, 원인 및 해결 방법 기록 |
| `Docs/Systems/엔티티 이동 시스템(FlowField).md` | HandleMoveRequestSystem, MovementArrivalSystem 설명에 ActionState 전환 반영 |

---

## 문제 원인

`HandleMoveRequestSystem`이 `UnitIntentState = Intent.Move`만 설정하고 `UnitActionState`는 변경하지 않았음. `VATAnimationStateUpdateSystem`은 `UnitActionState`를 기준으로 클립 인덱스를 결정하므로, Move 명령 후에도 `Action.Idle`이 유지되어 Walk 애니메이션이 트리거되지 않았음.

동일하게 `MovementArrivalSystem`도 도착 시 `Intent.Idle`만 설정하고 `Action.Idle`은 설정하지 않아, 다른 경로로 Moving이 설정된 경우 도착 후에도 Walk 상태가 유지될 수 있었음.

## 해결

- `HandleMoveRequestSystem`: `UnitActionState` ComponentLookup 추가, `ProcessRequest`에서 `Action.Moving` 설정
- `MovementArrivalSystem`: 유닛 도착 쿼리에 `RefRW<UnitActionState>` 추가, `Intent.Move` 도착 시 `Action.Idle` 동시 설정
