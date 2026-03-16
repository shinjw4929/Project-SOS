---
name: review-code
description: 코드 변경사항을 Project-SOS 프로젝트 컨벤션과 DOTS 규칙 기준으로 리뷰합니다. 사용자가 코드 리뷰, 변경점 검토, 또는 /review-code를 요청할 때 실행합니다.
allowed-tools: Read, Edit, Grep, Glob, Bash, Agent
---

## 코드 리뷰 실행

$ARGUMENTS가 있으면 해당 내용을 리뷰 범위/관점으로 반영한다.

### 1단계: 변경사항 수집

다음을 병렬로 실행하여 리뷰 대상을 파악한다:
- `git diff --name-only` (변경된 파일 목록)
- `git diff` (전체 변경 내용)
- 사용자가 특정 파일/커밋을 지정한 경우 해당 범위만 대상

변경된 `.cs` 파일만 리뷰 대상으로 삼는다. 문서, 메타, 설정 파일은 제외.

### 2단계: 컨텍스트 파악

변경된 파일이 속한 어셈블리(Client/Server/Shared/Authoring)와 관련 시스템을 파악한다.
필요 시 `Docs/` 폴더에서 관련 문서를 읽어 현재 시스템 구조를 확인한다.
변경된 코드 주변의 기존 코드도 함께 읽어 맥락을 파악한다.

### 3단계: 검토 항목

각 변경 파일에 대해 다음을 순서대로 검증한다. **실제 변경된 코드에만 집중**하며, 기존 코드의 문제를 지적하지 않는다.

#### A. DOTS 규칙

| 검토 항목 | 위반 조건 |
|-----------|-----------|
| Burst 컴파일 | 새 시스템/Job에 `[BurstCompile]` 누락 (입력/managed 제외) |
| Job System | 연산 로직이 메인 스레드에서 실행 (IJobEntity 미사용) |
| DamageEvent 패턴 | Health를 직접 수정 (`health.CurrentValue` 등) |
| ECB 사용 | 엔티티 생성/파괴/컴포넌트 추가·제거를 ECB 없이 수행 |
| Safe Lookup | `ComponentLookup[entity]` 직접 접근 (TryGetComponent 미사용) |
| 권한 최소화 | `RefRW<T>` 사용 시 `RefRO<T>`로 충분한 경우 |
| Tag 컴포넌트 | bool 필드로 상태 구분 (Tag + Query 필터링으로 대체 가능) |
| bool blittable | Burst struct에 `bool` 필드 + `ref` 전달 시 `[MarshalAs(UnmanagedType.U1)]` 누락 |
| BurstCompile static | `[BurstCompile]` static 메서드에서 struct 파라미터/반환 사용 (BC1064) |

#### B. 네트워크 아키텍처

| 검토 항목 | 위반 조건 |
|-----------|-----------|
| Server Authority | 게임 로직이 Client 어셈블리에서 실행 |
| Ghost 동기화 | 네트워크 동기화 필요한 컴포넌트에 `[GhostField]` 누락 |
| RPC 방향 | Client→Server 요청이 아닌 방향으로 게임 로직 RPC 전송 |
| Client/Server 분리 | Server 전용 로직이 Shared에 위치하거나 그 반대 |

#### C. 프로젝트 컨벤션

| 검토 항목 | 위반 조건 |
|-----------|-----------|
| 네이밍 - User | "Player" 사용 (Unity Player와 혼동, "User" 사용해야 함) |
| 네이밍 - 변수명 | 의미 불명의 축약 변수명 (`var c`, `var t` 등). 단, `ecb`, `em`, `job` 등 DOTS 관용어는 허용 |
| 네이밍 - 파일명 | 폴더별 네이밍 패턴 불일치 (Commands → `*InputSystem.cs`, RPCs → `*Rpc.cs` 등) |
| 중복 구현 | 기존 유틸리티(ArrivalUtility, CombatUtility, SpatialMaps 등)를 재구현 |
| 싱글톤 중복 | 기존 싱글톤을 확장할 수 있는데 새 싱글톤 생성 |
| Authoring 패턴 | Authoring 조합 패턴 불일치 (유닛: Movement+UnitMovement+Unit 등) |

#### D. 시스템 설계

| 검토 항목 | 위반 조건 |
|-----------|-----------|
| SystemGroup 배치 | 새 시스템의 Group 배치가 실행 순서상 부적절 |
| 의존성 선언 | 필요한 `UpdateAfter`/`UpdateBefore` 누락으로 데이터 레이스 가능 |
| CompleteDependency | 같은 SystemGroup 내에서 `CompleteDependency` 사용 (UpdateAfter로 대체 가능) |
| Job 스케줄링 충돌 | 동일 컴포넌트에 대한 ReadWrite 접근이 다른 시스템과 충돌 가능 |

#### E. 코드 품질

| 검토 항목 | 위반 조건 |
|-----------|-----------|
| 보안 | 커맨드 인젝션, 검증 없는 외부 입력 처리 |
| 성능 | Structural Change를 루프 내에서 반복, 불필요한 할당, O(n²) 탐색 (Spatial Map 사용 가능 시) |
| 엣지 케이스 | 엔티티 파괴, 연결 끊김, null Entity 미처리 |

### 4단계: 리뷰 결과 출력

다음 형식으로 결과를 출력한다:

```
## 코드 리뷰 결과

### 리뷰 대상
- 변경 파일 수: N개
- 변경 라인: +X / -Y

### 문제 발견

| # | 심각도 | 파일:라인 | 항목 | 설명 | 제안 |
|---|--------|-----------|------|------|------|
| 1 | 치명 | 파일:L## | ... | ... | ... |
| 2 | 경고 | 파일:L## | ... | ... | ... |
| 3 | 제안 | 파일:L## | ... | ... | ... |

### 잘된 점
- (컨벤션을 잘 따른 부분, 좋은 설계 판단)

### 최종 판단
(승인 가능 / 수정 필요)
```

**심각도 기준**:
- **치명**: 런타임 크래시, 데이터 레이스, 네트워크 비동기, Health 직접 수정
- **경고**: 컨벤션 위반, Burst 누락, 성능 문제, 누락된 의존성 선언
- **제안**: 더 나은 대안 존재, 가독성 개선, 사소한 네이밍 문제

### 5단계: 후속 행동

- **승인 가능**: 리뷰 결과만 출력하고 종료한다.
- **수정 필요**: 리뷰 결과 출력 후, 사용자에게 자동 수정 여부를 확인한다. 확인 시 치명/경고 항목을 코드에 직접 반영한다.

### 주의사항

- **변경된 코드만 리뷰**: 기존 코드의 문제를 지적하지 않는다. 단, 변경으로 인해 기존 코드와의 정합성이 깨지는 경우는 지적한다.
- **사실 기반**: 코드베이스에서 근거를 확인한 항목만 지적한다. 추측이나 과잉 지적은 하지 않는다.
- **효율 우선**: 사소한 스타일 이슈보다 런타임 영향이 큰 문제에 집중한다.
