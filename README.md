# Project-SOS

멀티플레이어 디펜스 RTS 게임. Unity DOTS 기반으로 수백 개의 엔티티가 동시에 움직이는 대규모 전투를 구현했습니다.

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
| **NavMesh Pathfinding** | 동적 장애물 회피 경로 탐색 |

## 주요 구현 사항

### 네트워크 아키텍처
- **Server-Authoritative**: 모든 게임 로직은 서버에서 처리, 클라이언트는 입력 전송 + 시각화만 담당
- **RPC 기반 명령 체계**: 이동, 공격, 건설, 생산 등 모든 유저 명령은 RPC로 서버에 요청
- **Ghost 기반 상태 동기화**: 유닛/건물/적의 위치, 체력 등 실시간 복제

### 전투 시스템
- **DamageEvent 버퍼 패턴**: 여러 시스템에서 발생하는 데미지를 버퍼에 누적 후 일괄 적용 (Job 스케줄링 충돌 방지)
- **통합 타겟팅 시스템**: Spatial Hash Map으로 범위 내 적/아군 탐색, 근접/원거리 공격 분리 처리
- **시각 전용 투사체**: 원거리 공격은 필중 + 별도의 시각 투사체 생성으로 네트워크 트래픽 최적화

### 이동 및 경로 탐색
- **동적 NavMesh Obstacle**: 건물 배치 시 런타임 장애물 생성/제거
- **Spatial Partitioning 충돌 회피**: 인접 유닛 간 밀어내기로 자연스러운 군집 이동
- **가속/감속 기반 이동**: 부드러운 출발과 정지

### 건설 및 생산
- **프리뷰 시스템**: 그리드 기반 배치 가능 여부 실시간 표시
- **사거리 기반 건설**: 즉시 건설 또는 이동 후 건설 자동 판단
- **생산 큐 시스템**: 복수 유닛 순차 생산

## 게임플레이

**Wave Defense** 형식의 생존 게임입니다.

- **Wave 0**: 초기 30마리의 적과 전투하며 기지 구축
- **Wave 1~2**: 시간 경과 또는 처치 수에 따라 웨이브 전환, 점점 강해지는 적 등장
- **적 종류**: 소형(빠름/근접), 대형(강함/근접), 비행(원거리/벽 무시)

### 조작
- **드래그 선택**: 좌클릭 드래그로 다수 유닛 선택
- **이동/공격**: 우클릭으로 이동, 적 우클릭으로 공격
- **건설**: 건설 유닛 선택 → Q → 건물 선택 → 배치
- **생산**: 생산 건물 선택 → Q → 유닛 선택

## 프로젝트 구조

```
Assets/Scripts/
├── Client/          # 입력 처리, UI, 시각화
├── Server/          # 게임 로직, 권한 검증
├── Shared/          # 공용 컴포넌트, RPC, 유틸리티
└── Authoring/       # GameObject → Entity 변환
```

자세한 코드 구조는 [Docs/코드베이스 구조.md](Docs/코드베이스%20구조.md) 참조.

## 시스템 흐름

```
입력 → RPC 전송 → 서버 명령 처리 → 타겟팅 → 이동 → 전투 → 사망 처리 → Ghost 동기화 → 클라이언트 렌더링
```

## 실행 환경

- **Unity 6000.0.64f1**
- **Entities 1.4.3** / **Netcode for Entities 1.10.0** / **Unity Physics 1.4.3**

### 설치

1. Unity Hub에서 프로젝트 열기
2. Package Manager에서 필수 패키지 확인:
   - `com.unity.entities`
   - `com.unity.entities.graphics`
   - `com.unity.netcode`
   - `com.unity.multiplayer.playmode`

### 에디터 설정

- **Enter Play Mode Settings**: Do not reload Domain or Scene
- **Player Settings**: Run in Background (체크)

## 문서

| 문서 | 내용 |
|------|------|
| [시스템 그룹 및 의존성](Docs/시스템%20그룹%20및%20의존성.md) | 시스템 실행 순서 |
| [엔티티 전투](Docs/엔티티%20전투.md) | 전투 로직 상세 |
| [건설 시스템](Docs/건설%20시스템.md) | 건물 배치 및 건설 |
| [자원 채집 시스템](Docs/자원%20채집%20시스템.md) | Worker 자원 수집 |
| [엔티티 이동 시스템](Docs/엔티티%20이동%20시스템(navmesh).md) | NavMesh 기반 이동 |

---