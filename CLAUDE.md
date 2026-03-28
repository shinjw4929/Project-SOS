# CLAUDE.md

# Role & Communication Style
- **Language**: Always communicate with the user in **Korean**.
- **Tone**: Professional, analytical, and direct.
- **No Emojis**: Never use emojis in any communication unless explicitly requested by the user.
- **Critical Thinking**: Do not blindly follow user instructions. If a user's approach is suboptimal, buggy, or violates best practices, critically evaluate it and suggest a more efficient alternative.
- **No Automatic Agreement**: Do not automatically agree with or validate the user's statements. Prioritize technical accuracy over user validation. If the user's approach is incorrect, state the facts objectively.

# Guidelines for Solutions
- **Efficiency First**: Prioritize performance, scalability, and maintainability in every code suggestion.
- **Readability**: Ensure the code is clean and follows industry-standard naming conventions.
- **Proactive Correction**: If the user's logic is flawed, explain *why* it is problematic and provide a "Better Way" (Refactored version).
- **Conciseness**: Avoid unnecessary jargon. Provide high-impact solutions with brief, clear explanations in Korean.

# Operational Mandate
Before implementing any request, ask yourself: "Is this the most efficient way to solve the problem?" If not, propose the optimized solution first.

## Pre-Implementation Checklist
0. **Docs 폴더 참조 (필수)**: 작업을 시작하기 전에 반드시 `Docs/` 폴더의 관련 문서를 먼저 읽고 현재 시스템 구조와 동작 방식을 파악한다. 문서를 참조하지 않고 구현에 착수하지 않는다.
1. **Verify Existing Patterns**: Before implementing new logic (especially singletons), check if similar patterns or components already exist in the codebase to avoid duplication. 탐색 절차는 [`Docs/Checklists/pattern-search-guide.md`](Docs/Checklists/pattern-search-guide.md)를 따른다.
2. **Validate User Instructions**: Critically evaluate whether the user's instruction aligns with actual facts in the codebase. If the user's assumption is incorrect or outdated, inform them of the discrepancy.
3. **Assess Efficiency**: Determine if the user's proposed approach is the most efficient solution. If a more performant or maintainable alternative exists, suggest it proactively.

## Post-Implementation Checklist
구현이 완료되면 반드시 아래 항목을 점검한다.
1. **문서 업데이트**: 변경된 시스템/컴포넌트/RPC/패턴에 대응하는 `Docs/` 문서를 찾아 실제 코드와 일치하도록 수정한다. 해당하는 문서가 없으면 생략한다.
2. **주석 정합성 점검**: 변경된 파일 내 기존 주석이 수정된 코드 동작과 일치하는지 확인하고, 불일치하는 주석을 수정 또는 제거한다. 새 주석은 로직이 자명하지 않은 경우에만 추가한다.
3. **CLAUDE.md 동기화**: 주요 패턴, 시스템 플로우, 네이밍 규칙 등 CLAUDE.md에 기재된 내용이 변경되었다면 함께 업데이트한다.
4. **작업 내용 기록** (선택): 계획 기반 작업은 `execution-log.md`에 기록되므로 생략. 계획 없이 수행한 단발 작업(핫픽스, 즉석 수정)만 `Docs/WorkLog/<날짜>/`에 기록한다.

---

## Project Reference

- **아키텍처 (구조, 시스템 플로우, 패턴, 게임 디자인)**: [Docs/Architecture.md](Docs/Architecture.md)
- **기획 방향성 (게임 컨셉, 설계 원칙, 감정 곡선)**: [Docs/GameDesign.md](Docs/GameDesign.md)
- **문서 업데이트 체크리스트 (Docs 폴더 구조, 업데이트 규칙)**: [Docs/Documentation-Checklist.md](Docs/Documentation-Checklist.md)

---

## Development Guidelines

### 기본 원칙
1. **Burst Compile 필수**: 모든 로직은 `[BurstCompile]` 적용 (예외: 입력 로직, managed 타입)
2. **Job System 활용**: 연산 로직은 `IJobEntity`로 구현하여 멀티스레드 활용
3. **네이밍**: "Player" 대신 **"User"** 사용 (Unity Player와 혼동 방지). 변수명은 의미를 알 수 있도록 작성하며, 축약하지 않는다 (예: `var c` ✗ → `var teamColor` ✓). 단, DOTS 관용 약어(`ecb`, `em`, `job` 등)는 허용.
4. **테스트**: EditMode(순수 함수) / PlayMode(ECS 시스템) 테스트 작성
5. **게임 규칙 하드코딩 금지**: 밸런스/규칙 관련 상수(거리, 시간, 확률, 횟수 등)는 시스템 코드에 직접 작성하지 않는다. 반드시 `GameSettings` 싱글톤에 필드를 추가하고 `GameSettingsAuthoring` 인스펙터에서 조절 가능하게 한다. fallback은 `SystemAPI.TryGetSingleton<GameSettings>(out var gs) ? gs.Field : DEFAULT` 패턴. Job 내부에서는 OnUpdate에서 읽어 구조체 필드로 전달. 유틸리티 static 메서드에서는 기본값 파라미터로 처리.

### Burst 제약사항
1. **`[BurstCompile]` static 메서드**: struct(`float3`, `Entity` 등)를 값으로 전달/반환하면 BC1064 에러 발생 (external function 제약). struct 파라미터/반환이 있는 메서드는 `[BurstCompile]` 제거하고 `[MethodImpl(AggressiveInlining)]`만 사용. primitive(`float`, `int`, `bool`)만 다루는 메서드만 개별 `[BurstCompile]` 적용 가능. 클래스 레벨 `[BurstCompile]`은 유지.
2. **`bool` 필드 blittable**: Burst 컴파일되는 struct에 `bool` 필드가 있고 `ref`로 전달되면 `[MarshalAs(UnmanagedType.U1)]` 필수. `[GhostField]`와 별개 목적.

### Unity DOTS Rules
1. **Safe Lookup**: `ComponentLookup<T>.TryGetComponent()`, `SystemAPI.TryGetSingleton<T>()`
2. **Minimize Permissions**: `RefRO<T>` 선호, `[ReadOnly]` Lookup 사용
3. **Tag Components**: bool 필드 대신 Tag 컴포넌트 + Query 필터링
4. **Null Checks**: `Entity.Null` 비교, `EntityManager.Exists()`, `.IsCreated` 프로퍼티
5. **ECB 사용**: 엔티티 생성/파괴/컴포넌트 변경은 `EndSimulationEntityCommandBufferSystem` 통해 처리
6. **싱글톤 초기화**: `ClientBootstrapSystem` 등 한 곳에 집중

### Combat System Rules
1. **DamageEvent 버퍼**: Health 직접 수정 금지 → DamageEvent 버퍼 사용 (상세: `Docs/Architecture.md` Key Patterns 참조)
2. **CompleteDependency 최소화**: 같은 SystemGroup 내에서는 `UpdateAfter`로 순서 지정
3. **AggroTarget**: 유닛/적 공통 타겟 추적 컴포넌트
4. **원거리 공격**: `RangedUnitTag`/`RangedEnemyTag` → 필중 + 시각 투사체(VisualOnlyTag) 생성

### Collider Rules
1. **Collider 용도**: raycast(선택, 건설 검증) + 투사체 충돌 전용. 물리 충돌에 Collider 사용 금지.
2. **물리 충돌**: 그리드 셀(GridCell.IsPathBlocked) 기반. PredictedMovementSystem, GridObstacleResponseSystem 참조.
3. **Collider 크기**: 유닛/적 Capsule 반지름 ≈ ObstacleRadius, 건물 Box ≈ Width × Length × CellSize.

