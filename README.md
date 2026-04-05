# Project-SOS

1~8인 협동 웨이브 디펜스 RTS 게임. Unity DOTS 기반으로 수백 개의 엔티티가 동시에 움직이는 대규모 전투를 구현했습니다. C++ 룸 서버를 통해 매칭하고, Server-Authoritative 아키텍처로 모든 게임 로직을 서버에서 처리합니다.

<!--
스크린샷 또는 GIF 추가 예정
![gameplay](docs/images/gameplay.gif)
-->

## 핵심 기술

| 기술 | 용도 |
|------|------|
| **Unity ECS (Entities 1.4)** | 데이터 지향 설계로 대량 엔티티 처리 |
| **Netcode for Entities** | 클라이언트-서버 동기화, Ghost 복제 |
| **Burst Compiler + Job System** | 멀티스레드 병렬 처리 |
| **Spatial Hashing** | O(1) 인접 엔티티 탐색 (타겟팅/충돌 회피) |
| **Flow Field 경로 탐색** | BFS 기반 Flow Field 계산 + Grid 장애물 시스템 |
| **VAT (Vertex Animation Texture)** | GPU 기반 수천 유닛 동시 애니메이팅 (Animator 불필요) |
| **C++ Room/Chat Server** | Protobuf 기반 TCP 룸 매칭 + 실시간 채팅 |

## 주요 구현 사항

### 네트워크 아키텍처
- **Server-Authoritative**: 모든 게임 로직은 서버에서 처리, 클라이언트는 입력 전송 + 시각화만 담당
- **RPC 기반 명령 체계**: 이동, 공격, 건설, 생산 등 모든 유저 명령은 RPC로 서버에 요청
- **Ghost 기반 상태 동기화**: 유닛/건물/적의 위치, 체력 등 실시간 복제
- **Ghost Relevancy**: 뷰포트 기반 AABB 필터링으로 불필요한 Ghost 전송 제외
- **미니맵 RPC**: MinimapBatchRpc로 적/유닛 위치를 분산 전송, 클라이언트에서 Double Buffer 스왑 후 Texture2D 렌더링

### 룸 서버 연동
- **C++ 룸 서버 (TCP :8080)**: 로비/대기실 관리, GameStart 시 AuthToken + SessionId 발급
- **토큰 검증**: 서버가 룸 서버 :8081로 토큰 검증 후 Hero 생성 허용
- **슬롯 관리**: 연결 끊김 시 SlotReleased 전송 + 30초 하트비트
- **Protobuf 프로토콜**: Sos.Room 네임스페이스, 4byte LE length-prefix 프레이밍

### 채팅 시스템
- **C++ 채팅 서버 (TCP :8082)**: 로비/인게임 실시간 텍스트 채팅
- **채널 자동 전환**: LOBBY(대기실) → ALL(인게임 전체)
- **입력 격리**: 채팅 입력 중 게임 키보드 입력 차단 (마우스 유지)

### 전투 시스템
- **DamageEvent 버퍼 패턴**: 여러 시스템에서 발생하는 데미지를 버퍼에 누적 후 일괄 적용 (Job 스케줄링 충돌 방지)
- **통합 타겟팅 시스템**: Spatial Hash Map으로 범위 내 적/아군 탐색, 근접/원거리 공격 분리 처리
- **시각 전용 투사체**: 원거리 공격은 필중 + 별도의 시각 투사체 생성으로 네트워크 트래픽 최적화
- **사망 연출**: 서버에서 Dying 상태 + DeathTimer 부착, 클라이언트에서 EntityTiltSystem이 점진적 전방 기울임 연출 후 엔티티 파괴

### 이동 및 경로 탐색
- **Flow Field 기반 Pathfinding**: BFS 기반 Flow Field 계산, LRU 캐시(32x2풀) + 8 IJob 병렬 처리
- **Grid 기반 장애물 시스템**: 건물 배치 시 그리드 셀 차단, 경로 무효화 및 재계산
- **Spatial Partitioning 충돌 회피**: 인접 유닛 간 밀어내기로 자연스러운 군집 이동
- **Flow Field Steering**: Flow Field 방향 벡터 기반 조향 + 도착 판정

### 건설 및 생산
- **프리뷰 시스템**: 그리드 기반 배치 가능 여부 실시간 표시
- **사거리 기반 건설**: 즉시 건설 또는 이동 후 건설 자동 판단
- **생산 큐 시스템**: 복수 유닛 순차 생산
- **건물 종류**: Wall(무적 방벽), Barracks(공격 유닛 생산), Turret(자동 공격 타워), ResourceCenter(자원 반납 + Worker 생산)

### 애니메이션 및 사운드
- **VAT 애니메이션**: 에디터 툴로 스켈레탈 애니메이션 → Position Texture + Static Mesh로 베이킹, 서버가 상태→클립 매핑 후 Ghost 동기화, 클라이언트 셰이더가 텍스처 룩업으로 버텍스 변형 (대상: Hero, EnemySmall, EnemyFlying)
- **엔티티 기울임**: PostTransformMatrix 기반 pitch 기울임 — 공격 시 swing-return 사이클, 사망 시 점진적 전방 기울임 (VAT 유무 무관, 전체 유닛/적 대상)
- **SoundEvent 패턴**: ECS 버퍼 → MonoBehaviour 브릿지, 상태 변화 감지로 공격/사망/스폰 사운드 재생 (AudioSource 풀)

## 게임플레이

**1~8인 협동 웨이브 디펜스** 생존 게임입니다. 맵 중앙에서 쏟아지는 적 웨이브를 벽과 유닛으로 막아냅니다.

- **핵심 루프**: 자원 채집 → 벽 건설 / 유닛 생산 → 웨이브 방어 → 반복
- **Wave 0**: EnemyBig 30마리 즉시 스폰, 벽으로 차단하며 기지 구축
- **Wave 1~2**: 시간 경과 또는 처치 수에 따라 웨이브 전환, 소형/비행 적 등장
- **승리**: 모든 웨이브 종료 시 승리
- **패배**: 모든 플레이어의 영웅이 사망하면 패배

### 유닛
| 유닛 | 역할 |
|------|------|
| **Hero** | 근접 탱킹, 자원 반납 가능 |
| **Worker** | 자원 채집 전담, 건설 담당 |
| **Tank** | 높은 체력, 전선 유지 |
| **Striker** | 빠르고 약함, 기동 대응 |
| **Archer** | 초장거리 후방 화력 |

### 적
| 적 | 특성 |
|------|------|
| **EnemySmall** | 빠름, 근접, 벽 틈 통과 |
| **EnemyBig** | 강함, 근접, 벽으로 차단 가능 |
| **EnemyFlying** | 원거리, 공중, 벽 무시 |

### 조작
- **드래그 선택**: 좌클릭 드래그로 다수 유닛 선택
- **이동/공격**: 우클릭으로 이동, 적 우클릭으로 공격
- **건설**: Worker 선택 → Q → 건물 선택 → 배치
- **생산**: 생산 건물 선택 → Q → 유닛 선택

## 프로젝트 구조

```
Assets/Scripts/
├── Client/          # 입력 처리, UI, 시각화, 애니메이션/사운드
│   ├── Controller/  # MonoBehaviour (카메라, 사운드, 미니맵, 룸/채팅 UI)
│   ├── Systems/     # 클라이언트 ECS 시스템
│   └── UI/          # UI 컴포넌트
├── Server/          # 게임 로직, 권한 검증, 웨이브 관리
│   ├── Network/     # 룸 서버 연동, 토큰 검증
│   └── Systems/     # 서버 ECS 시스템
├── Shared/          # 공용 컴포넌트, RPC, 유틸리티
│   ├── Components/  # ECS 컴포넌트 (이동, 전투, 상태, 선택 등)
│   ├── Network/     # Protobuf, 룸/채팅 클라이언트
│   ├── RPCs/        # 네트워크 RPC 정의
│   └── Systems/     # 공용 시스템 (Flow Field, Spatial Partitioning 등)
└── Authoring/       # GameObject → Entity 변환 (베이킹)
```

자세한 코드 구조는 [Docs/Systems/코드베이스 구조.md](Docs/Systems/코드베이스%20구조.md) 참조.

## 시스템 흐름

```
[입력] 유닛 선택/명령 → [RPC] 서버 전송
  → [공간 분할] SpatialMap 빌드 → [명령 처리] 이동/공격/건설 목표 설정
  → [타겟팅] 적↔아군 자동 탐색 → [이동] Flow Field 경로 + 충돌 회피
  → [전투] 근접/원거리 공격 → DamageEvent → 일괄 적용
  → [사망] Dying 상태 + DeathTimer → 엔티티 파괴
  → [Ghost 동기화] → [클라이언트] VAT 애니메이션 + 기울임 + 사운드 + 렌더링
```

## 실행 환경

- **Unity 6000.0.67f1**
- **Entities 1.4.3** / **Netcode for Entities 1.11.0** / **Unity Physics 1.4.4**
- **Burst 1.8.27** / **URP 17.0.4**

### 설치

1. Unity Hub에서 프로젝트 열기
2. Package Manager에서 필수 패키지 확인:
   - `com.unity.entities` / `com.unity.entities.graphics`
   - `com.unity.netcode`
   - `com.unity.physics`
   - `com.unity.burst`
   - `com.unity.render-pipelines.universal`

### 에디터 설정

- **Enter Play Mode Settings**: Do not reload Domain or Scene
- **Player Settings**: Run in Background (체크)

## 문서

| 문서 | 내용 |
|------|------|
| [Architecture](Docs/Architecture.md) | 프로젝트 구조, 시스템 플로우, 핵심 패턴 |
| [코드베이스 구조](Docs/Systems/코드베이스%20구조.md) | 전체 파일/폴더 구조 |
| [시스템 그룹 및 의존성](Docs/Systems/시스템%20그룹%20및%20의존성.md) | 시스템 실행 순서 |
| [엔티티 선택 시스템](Docs/Systems/엔티티%20선택%20시스템.md) | 유닛/건물 선택 로직 |
| [엔티티 이동 시스템](Docs/Systems/엔티티%20이동%20시스템(FlowField).md) | Flow Field 기반 이동 |
| [엔티티 전투](Docs/Systems/엔티티%20전투.md) | 전투 로직 상세 |
| [건설 시스템](Docs/Systems/건설%20시스템.md) | 건물 배치 및 건설 |
| [자원 채집 시스템](Docs/Systems/자원%20채집%20시스템.md) | Worker 자원 수집 |
| [유저 자원, 인구수](Docs/Systems/유저%20자원,%20인구수.md) | 경제 시스템 |
| [상태 시스템 설계](Docs/Systems/Project-SOS%20상태%20시스템%20설계.md) | UI 상태 머신 |
| [미니맵 및 Ghost Relevancy](Docs/Systems/미니맵%20및%20Ghost%20Relevancy.md) | Ghost Relevancy, 미니맵 RPC |
| [팀 색상 시스템](Docs/Systems/팀%20색상%20시스템.md) | 팀별 색상 틴트 |
| [로깅 시스템](Docs/Systems/로깅%20시스템.md) | 로깅 카테고리, SOSLog |
| [채팅 시스템](Docs/Systems/채팅%20시스템.md) | TCP 기반 실시간 텍스트 채팅 |
| [룸 서버 연동](Docs/Systems/룸%20서버%20연동.md) | C++ 룸 서버와 Unity 클라이언트 연동 |

---