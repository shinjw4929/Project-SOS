# 미니맵 및 Ghost Relevancy

## 개요

2400+ Ghost 스냅샷 대역폭 포화 문제를 해결하기 위해 두 시스템을 도입:
1. **Ghost Relevancy**: 카메라에서 먼 적 Ghost를 전송하지 않아 대역폭 절감
2. **UI 미니맵**: Relevancy로 제외된 적의 위치를 별도 RPC로 전달, Texture2D에 점으로 렌더링

---

## 1. Ghost Relevancy

### 동작 원리

```
[Client] CameraSystem (~20Hz)
    │ CameraPositionRpc (Y=0, XZ 평면 좌표)
    ↓
[Server] UpdateConnectionPositionSystem
    │ RPC 수신 → GhostConnectionPosition에 카메라 위치 반영
    ↓
[Server] GhostRelevancySystem (UpdateAfter: UpdateConnectionPositionSystem)
    │ 카메라 기준 거리로 적 Ghost 전송 여부 결정
    │ SetIsIrrelevant 모드 (blacklist 방식)
    ↓
[Netcode] GhostSendSystem
    │ irrelevant Ghost → 스냅샷에서 제외 → 클라이언트에 전송 안 함
    ↓
[Client] 해당 적 엔티티 despawn (파괴)
```

### AABB 기반 Relevancy + Hysteresis

원형(distancesq) 대신 **직사각형(AABB)** 검사 사용. 16:9 비율에서 원형 대비 커버 면적 ~45% 감소 → relevant Ghost 수 절반.

| 파라미터 | 값 | 설명 |
|---------|-----|------|
| OuterMultiplier | 1.3 | ViewHalfExtent × 1.3 밖 → irrelevant (전송 차단) |
| InnerMultiplier | 1.15 | ViewHalfExtent × 1.15 안 → relevant (전송 복원) |
| 버퍼 존 | 15% | 경계에서 반복 전환 방지 |

- **ViewHalfExtent**: 클라이언트가 뷰포트 4코너를 y=0 평면에 투영하여 계산한 반크기(halfX, halfZ)
- 해상도/비율 자동 대응: 각 클라이언트가 자기 화면 기준으로 계산해서 서버에 전송
- Y좌표는 0으로 전송하여 높이 차이가 거리 계산에 영향 주지 않음
- `ConnectionViewExtent` 컴포넌트에 Connection별 반크기 저장

### irrelevant 상태의 영향

| 항목 | 서버 | 클라이언트 |
|------|------|-----------|
| 엔티티 존재 | 정상 존재 | **despawn (파괴)** |
| 이동/충돌 | 정상 시뮬레이션 | 불가 (엔티티 없음) |
| 전투 | 정상 (데미지, 타겟팅) | 불가 |
| 미니맵 표시 | - | MinimapBatchRpc로 표시 |

### relevant 복원 시

- 서버가 다음 스냅샷에 포함 → 클라이언트에서 새로 spawn
- 네트워크 틱(30Hz) + importance scaling에 따라 **1~수 프레임 지연**
- 10m 버퍼 존 덕분에 카메라 가시 영역 진입 전에 이미 relevant 전환

---

## 2. 미니맵 시스템

### 아키텍처

```
[Server] MinimapDataBroadcastSystem
    │ 전체 적 위치 수집 (EnemyTag + LocalTransform)
    │ 32개씩 MinimapBatchRpc로 분할
    │ 매 틱 2배치 전송 (~60틱에 걸쳐 분산)
    ↓
[Client] MinimapDataReceiveSystem
    │ RPC 수신 → PendingPositions 버퍼에 복사
    │ 전체 수신 완료 시 EnemyPositions ↔ PendingPositions 스왑
    ↓
[Client] MinimapRenderer (MonoBehaviour)
    │ 100ms마다 Texture2D(256x256) 렌더링
```

### 렌더링 계층 (아래에서 위 순서)

| 순서 | 대상 | 데이터 소스 | 색상 | 점 크기 |
|------|------|-----------|------|---------|
| 1 | 적 | MinimapDataState (RPC) | 빨강 | 1px |
| 2 | 자원 노드 | Ghost 쿼리 (ResourceNodeTag) | 노랑 | 2px |
| 3 | 아군 유닛 | Ghost 쿼리 (UnitTag + GhostInstance) | 초록 | 2px |
| 4 | 건물 | Ghost 쿼리 (StructureTag) | 파랑 | 3px |
| 5 | 히어로 | Ghost 쿼리 (HeroTag) | 흰색 | 4px |

- 적: **RPC 데이터** 사용 (irrelevant 적도 포함)
- 나머지: **Ghost 엔티티 직접 쿼리** (항상 클라이언트에 존재)
- 맵 범위: `CameraSettings.MapBoundsMin/Max` 기반 UV 좌표 변환

### MinimapBatchRpc 구조

| 필드 | 타입 | 용도 |
|------|------|------|
| FrameId | uint | 프레임 식별 (배치 그룹핑) |
| StartIndex | ushort | 전체 적 목록 내 시작 인덱스 |
| TotalCount | ushort | 전체 적 수 |
| ValidCount | byte | 이 배치의 유효 엔트리 수 (0~32) |
| P00~P31 | float2 x32 | xz 좌표 |

### Double Buffer 패턴 (MinimapDataState)

```
수신 중:  PendingPositions에 배치 데이터 복사
완료 시:  EnemyPositions ↔ PendingPositions 스왑
렌더링:   EnemyPositions에서 읽기 (수신과 독립)
```

- 적 0마리: `TotalCount=0` RPC 1회 전송 → 클라이언트 미니맵 클리어
- 새 FrameId 감지: PendingPositions Resize + ReceivedCount 리셋

---

## 3. 대역폭

| 항목 | 값 |
|------|-----|
| MinimapBatchRpc 크기 | 헤더 9B + 32×8B = 265B |
| 2400적 기준 RPC 수 | 75 RPCs/초 |
| 대역폭 | ~20KB/s per connection |
| CameraPositionRpc | 20B × 20Hz = ~400B/s per connection |

### DefaultSnapshotPacketSize

기본 MTU(~1400B) 사용. AABB Relevancy로 relevant Ghost를 ~200-400으로 제한하므로 MTU 패킷으로 충분.
4096 사용 시 UDP 3개로 분할되어 fragment 유실 → 전체 스냅샷 소실 위험이 있었다.

---

## 4. 관련 파일

| 파일 | 역할 |
|------|------|
| `Shared/RPCs/MinimapBatchRpc.cs` | 미니맵 배치 RPC |
| `Shared/RPCs/CameraPositionRpc.cs` | 카메라 위치 + 뷰포트 반크기 전송 RPC |
| `Shared/Components/ConnectionViewExtent.cs` | Connection별 뷰포트 반크기 컴포넌트 |
| `Client/Component/Singleton/MinimapDataState.cs` | 미니맵 데이터 싱글톤 |
| `Client/Systems/MinimapDataReceiveSystem.cs` | RPC 수신 → 싱글톤 갱신 |
| `Client/Controller/UI/MinimapRenderer.cs` | Texture2D 점 렌더링 |
| `Client/Controller/Camera/CameraSystem.cs` | 카메라 위치 + 뷰포트 반크기 RPC 전송 (~20Hz) |
| `Client/Controller/UI/Info/EntityCountRenderer.cs` | 적 수 표시 (MinimapDataState 사용) |
| `Server/Systems/MinimapDataBroadcastSystem.cs` | 적 위치 수집 + 배치 전송 |
| `Server/Systems/GhostRelevancySystem.cs` | Ghost Relevancy AABB 필터링 (ViewHalfExtent × 배율) |
| `Server/Systems/UpdateConnectionPositionSystem.cs` | 카메라 위치 → GhostConnectionPosition + ConnectionViewExtent |

---

## 5. 씬 설정

InGame.unity Canvas 하위:
- `RawImage` → MinimapRenderer.minimapImage에 할당
- MinimapRenderer 컴포넌트 부착
- 앵커: 좌하단 또는 우하단
