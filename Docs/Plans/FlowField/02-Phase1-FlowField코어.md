# Phase 1: Flow Field 코어 알고리즘

**신규 파일**: `Assets/Scripts/Shared/Utilities/FlowFieldCore.cs`

> **위치 결정**: `Shared/Utilities/`에 배치. 순수 BFS 알고리즘이므로 Server 전용일 이유 없고, EditMode 테스트에서 접근 가능해야 함 (EditModeTests.asmdef이 Shared를 참조).

---

## 구조

```csharp
[BurstCompile]  // struct 레벨 [BurstCompile] 유지
struct FlowFieldCore
{
    // BFS 기반 Flow Field 계산
    // struct 파라미터(int2, NativeArray)로 인해 메서드 레벨 [BurstCompile] 불가 (BC1064)
    // IJob 내부에서 호출되므로 Job 레벨 Burst에 의해 자동 인라인됨
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void ComputeField(
        NativeArray<byte> passabilityMap,
        int2 destination,
        int2 gridSize,
        NativeArray<byte> outputField)
}
```

---

## 방향 상수 위치

방향 인코딩 상수(0-7, 255)와 방향→오프셋 변환 테이블은 `FlowFieldCore` 내부에 정의한다. `FlowFieldSteeringSystem`도 같은 `Shared` 어셈블리이므로 접근에 문제 없음.

---

## BFS 알고리즘

1. 목적지 셀에서 출발
2. NativeQueue로 BFS 확산
3. 8방향 인접 셀 탐색
4. 각 셀에 방향(byte) 기록 — **셀 B가 셀 A에서 확산된 경우, B에 기록되는 방향은 "B에서 A로 가는 방향"(확산 방향의 역방향)**
   - 예: 목적지에서 N(위쪽)으로 확산하여 셀 X에 도달 → 셀 X의 방향은 S(4) (X에서 S로 가면 목적지에 가까워짐)
5. 도달 불가 셀은 255(None)

### 대각 이동 코너 차단
대각 확산 시 인접 직교 셀이 막혀있으면 확산 불가:
```
NE로 이동하려면 → N과 E 모두 passable이어야 함
SE로 이동하려면 → S와 E 모두 passable이어야 함
(나머지 대각도 동일)
```

---

## 방향 인코딩 (byte)

```
0 = N    (0, +1)
1 = NE   (+1, +1)
2 = E    (+1, 0)
3 = SE   (+1, -1)
4 = S    (0, -1)
5 = SW   (-1, -1)
6 = W    (-1, 0)
7 = NW   (-1, +1)
255 = None (도달 불가 / 목적지 자체)
```

---

## 워커 메모리 (Persistent, 재사용)

BFS 실행마다 할당/해제하지 않고 Persistent로 유지.
**소유권**: FlowFieldSystem이 `OnCreate`에서 8세트 할당, `FlowFieldComputeJob`에 전달. FlowFieldCore는 stateless 유틸리티 struct.

| 버퍼 | 타입 | 크기 (100x100 기준) |
|------|------|-------------------|
| BFS queue | `NativeQueue<int2>` | 최대 10,000 |
| visited | `NativeArray<byte>` | 10,000 bytes (byte 배열, 단순성 우선) |
| cost map | `NativeArray<ushort>` | 20,000 bytes |

cost map은 BFS 거리를 저장하여 방향 결정에 사용.

---

## 체크리스트

- [ ] `FlowFieldCore` struct 작성 (`Shared/Utilities/FlowFieldCore.cs`)
- [ ] `ComputeField` BFS 구현 (방향 = 확산 역방향 기록)
- [ ] 8방향 확산 + 대각 코너 차단 로직
- [ ] 방향 인코딩 상수 + 방향→오프셋 변환 테이블 정의
- [ ] `ComputeField`에 `[MethodImpl(AggressiveInlining)]` 적용 (struct 파라미터로 개별 `[BurstCompile]` 불가)
- [ ] 워커 메모리 Persistent 할당 (FlowFieldSystem.OnCreate에서 8세트)
