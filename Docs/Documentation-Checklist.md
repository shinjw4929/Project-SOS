# 문서 업데이트 체크리스트

## Docs 폴더 구조
```
Docs/
├── Architecture.md                # 프로젝트 구조, 시스템 플로우, 핵심 패턴
├── GameDesign.md                  # 기획 방향성 (게임 컨셉, 설계 원칙, 감정 곡선)
├── Documentation-Checklist.md     # 이 파일
│
├── Systems/                       # ECS 시스템 기능 문서
│   ├── 코드베이스 구조.md              # 전체 파일/폴더 구조, 어셈블리 목록
│   ├── 시스템 그룹 및 의존성.md         # SystemGroup 정의, 시스템 간 의존성
│   ├── 엔티티 선택 시스템.md            # 선택 Phase, SelectionRing, 관련 컴포넌트
│   ├── 엔티티 이동 시스템(navmesh).md   # NavMesh, PathfindingSystem, MovementGoal
│   ├── 엔티티 전투.md                  # 공격 시스템, DamageEvent, AggroTarget
│   ├── 건설 시스템.md                  # 건물 배치, BuildRequestRpc, 그리드 점유
│   ├── 자원 채집 시스템.md              # 자원 수집, CarriedResource, 반납 로직
│   ├── 유저 자원, 인구수.md             # UserEconomy, Population 시스템
│   ├── Project-SOS 상태 시스템 설계.md  # UserContext 상태 머신, UI 상태
│   ├── 팀 색상 시스템.md                # TeamColorSystem, TeamColorPalette, 팀별 틴트
│   ├── 미니맵 및 Ghost Relevancy.md    # Ghost Relevancy, 미니맵 RPC 시스템, 대역폭
│   ├── 로깅 시스템.md                  # LogCategory, SOSLog, 로깅 규칙
│   └── 룸 서버 연동.md                 # RoomClient, 토큰 검증, 접속 흐름, Protobuf
│
├── Checklists/                    # 스킬 참조용 체크리스트
│   └── review-code-checklist.md   # 코드 리뷰 상세 검토 항목 (A~E)
│
├── Plans/                         # 구현 계획 문서
│   ├── FlowField/                 # NavMesh→Flow Field 전환 계획 (Phase 0~5)
│   ├── Logging/                   # 로깅 시스템 Tier2 (ClickHouse, Grafana)
│   ├── RoomServer/                # 룸 서버 연동 계획
│   ├── Animation/                 # VAT 애니메이션 + 사운드 시스템 (Phase 1~6)
│   └── ChatServer/                # 채팅 서버 구축 계획
│
└── WorkLog/                       # 날짜별 작업 기록
    └── 2026-03-13/                # 로깅 시스템 Tier1 구현
```

---

## 변경 유형별 업데이트 대상

| 변경 유형 | 업데이트 대상 문서 (Systems/ 하위) |
|----------|----------------------------------|
| 새 시스템 추가 | `시스템 그룹 및 의존성.md`, `코드베이스 구조.md` |
| 새 컴포넌트 추가 | `코드베이스 구조.md`, 관련 기능 문서 |
| 선택 로직 변경 | `엔티티 선택 시스템.md` |
| 이동 로직 변경 | `엔티티 이동 시스템(navmesh).md` |
| 전투 로직 변경 | `엔티티 전투.md` |
| 건설 로직 변경 | `건설 시스템.md` |
| 자원/채집 변경 | `자원 채집 시스템.md`, `유저 자원, 인구수.md` |
| UI 상태 변경 | `Project-SOS 상태 시스템 설계.md` |
| 게임 규칙/밸런스 방향 변경 | `GameDesign.md` |
| 로깅 변경 | `로깅 시스템.md` |
| 룸 서버/접속 흐름 변경 | `룸 서버 연동.md` |
| 새 RPC 추가 | `코드베이스 구조.md` (RPCs 섹션), 관련 기능 문서 |

---

## 문서 작성 원칙

1. **코드와 동기화**: 문서 내용이 실제 코드와 일치해야 함
2. **간결함 유지**: 핵심 로직과 데이터 흐름 중심으로 작성
3. **예제 포함**: 복잡한 패턴은 코드 예제로 설명
4. **CLAUDE.md 동기화**: 주요 패턴/플로우 변경 시 CLAUDE.md도 함께 업데이트
