# VAT 애니메이션 + 사운드 시스템 구현 계획

## Context

Project-SOS는 수천 유닛이 동시에 활동하는 DOTS 기반 RTS 게임이다. 현재 애니메이션과 사운드가 전혀 구현되어 있지 않다 (정적 MeshRenderer만 사용). 수천 마리 규모를 처리해야 하므로 Hybrid Animator(MonoBehaviour) 방식은 성능상 불가능하며, **GPU Animation (Vertex Animation Texture)** 방식이 필수다.

**전제 조건**: 스켈레탈 애니메이션이 포함된 FBX 모델이 필요하다. 현재 게임 유닛(Hero, Worker, Striker 등)은 정적 메시만 사용 중이므로, 애니메이션 포함 모델 확보가 선행되어야 한다.

---

## Part 1: VAT 애니메이션 시스템

### 1.1 VAT 개요

스켈레탈 애니메이션을 텍스처에 베이킹하여, 런타임에서 셰이더가 텍스처 룩업만으로 버텍스 위치를 결정하는 방식. Animator/SkinnedMeshRenderer를 완전히 제거하고, 일반 MeshRenderer + 커스텀 셰이더로 수천 엔티티를 GPU에서 동시에 애니메이팅한다.

### 1.2 오프라인 베이킹 파이프라인 (Editor 툴)

**입력**: SkinnedMeshRenderer + AnimationClip이 있는 FBX 모델
**출력**:
- Position Texture (RGBAHalf): 행=프레임, 열=버텍스, 값=XYZ 좌표
- VATClipDataAsset (ScriptableObject): 클립별 메타데이터
- Static Mesh: 바인드포즈 정적 메시 (런타임 MeshFilter용)

```
텍스처 레이아웃 (예: 버텍스 500개, 총 120프레임):
너비 = 500 (버텍스 수)
높이 = 120 (총 프레임)
[Idle:   행 0-29]   30fps, loop
[Walk:   행 30-59]  30fps, loop
[Attack: 행 60-89]  30fps, no loop
[Work:   행 90-119] 30fps, loop
```

**생성 파일**:
- `Assets/Editor/VATBaker/VATBakerWindow.cs` - EditorWindow UI
- `Assets/Editor/VATBaker/VATBakeUtility.cs` - 베이킹 로직 (AnimationClip.SampleAnimation -> BakeMesh -> Texture2D)

### 1.3 URP 셰이더

**생성 파일**: `Assets/Shaders/VATAnimation.shader` (URP Lit 기반 HLSL)

핵심 로직:
```hlsl
// Per-entity 프로퍼티 (Entities Graphics MaterialPropertyComponent)
float4 _VATAnimParam; // x=normalizedTime, y=clipStartRow, z=clipRowCount, w=reserved

// 버텍스 셰이더
float u = (vertexIndex + 0.5) * _VATTexelSize.x;  // 버텍스 인덱스 → U (UV2에서 인코딩)
float v = _VATAnimParam.y + _VATAnimParam.x * _VATAnimParam.z;  // 클립 내 위치 → V

// 프레임 간 보간
float3 posA = tex2Dlod(_VATPositionTex, float4(u, vFloor, 0, 0)).xyz;
float3 posB = tex2Dlod(_VATPositionTex, float4(u, vCeil, 0, 0)).xyz;
float3 animatedPos = lerp(posA, posB, frac(frameFloat));
```

- 기존 `_BaseColor` 프로퍼티 유지 → TeamColorSystem 호환성 보장
- UV2 채널에 버텍스 인덱스 인코딩 (베이킹 시 생성, SV_VertexID 대체)

### 1.4 ECS 컴포넌트

**Shared 어셈블리**:

| 파일 | 컴포넌트 | 역할 |
|------|---------|------|
| `Shared/Components/Animation/VATAnimationState.cs` | `VATAnimationState` | **유닛/적 공통** Ghost 동기화. `byte CurrentClipIndex` + `float AnimStartTime` |
| `Shared/Components/Animation/VATClipLibrary.cs` | `VATClipLibrary` | BlobAssetReference로 클립 메타데이터 참조 |
| `Shared/Animation/VATClipDataAsset.cs` | ScriptableObject | 에디터에서 클립 정보 + 텍스처 참조 관리 |

**Client 어셈블리**:

| 파일 | 컴포넌트 | 역할 |
|------|---------|------|
| `Client/Components/Animation/VATAnimParam.cs` | `VATAnimParam` | `[MaterialProperty("_VATAnimParam")]` per-entity 셰이더 파라미터 |

**네트워크 전략**:
- **유닛/적 공통**: `VATAnimationState` (Ghost 동기화, ~3바이트/엔티티)
- 서버가 UnitActionState/EnemyState 변화 시 VATAnimationState를 갱신
- Ghost Relevancy로 뷰포트 밖 엔티티는 이미 필터링되므로 대역폭 영향 미미
- PlaybackSystem이 단일 쿼리로 통합 가능 (유닛/적 분기 불필요)

### 1.5 ECS 시스템

**서버**: `Server/Systems/Animation/VATAnimationStateUpdateSystem.cs`
- 그룹: `SimulationSystemGroup`, **UpdateAfter: `FixedStepSimulationSystemGroup`**
- UnitActionState/EnemyState 변화 → VATAnimationState.CurrentClipIndex + AnimStartTime 갱신
- 유닛 클립 매핑: Idle→0, Moving→1, Working→2, Attacking→3, Dying→4, Dead→5, Disabled→6
- 적 클립 매핑: Idle/Dormant→0, Wandering/Chasing→1, Attacking→2, Dying→3, Dead→4, Disabled→5
- **이유**: UnitActionState는 SimulationSystemGroup(WorkerGatheringSystem 등)과 FixedStepSimulationSystemGroup(MeleeAttackSystem) 양쪽에서 변경됨. FixedStep 이후 실행해야 양쪽 변경 모두 캡처 가능

**클라이언트**: `Client/Systems/Animation/VATAnimationInitSystem.cs`
- 그룹: `SimulationSystemGroup`, UpdateBefore: `VATAnimationPlaybackSystem`
- TeamColorSystem Phase 1 패턴 활용 (Parent 체인 탐색)
- 새 메시 엔티티 감지 → VATAnimParam 부착

**클라이언트**: `Client/Systems/Animation/VATAnimationPlaybackSystem.cs`
- 그룹: `SimulationSystemGroup`, UpdateBefore: `TeamColorSystem`
- **단일 쿼리**: VATAnimationState에서 CurrentClipIndex + AnimStartTime 읽기 (유닛/적 공통)
- VATClipLibrary(BlobAssetReference)에서 클립 메타데이터 조회
- ElapsedTime - AnimStartTime으로 진행률 계산 → VATAnimParam.Value 갱신
- 루프 클립: fmod, 비루프: clamp (마지막 프레임 유지)
- IJobEntity + [BurstCompile] + ScheduleParallel

### 1.6 Authoring

`Authoring/Animation/VATAnimationAuthoring.cs`:
- VATClipDataAsset 참조 → BlobAssetReference<VATClipBlobData> 생성
- VATAnimationState + VATClipLibrary 부착 (유닛/적 공통, 구분 로직 불필요)
- **기존 UnitAuthoring/EnemyAuthoring 수정 불필요** (Composition 패턴)

### 1.7 프리팹 변경

```
변경 전:                                변경 후:
Hero (Root)                             Hero (Root)
├── *Authoring 컴포넌트들               ├── *Authoring + VATAnimationAuthoring
└── Child (MeshFilter + MeshRenderer)   └── Child (MeshFilter(VAT 메시) + MeshRenderer(VAT 머티리얼))
```

---

## Part 2: 사운드 시스템

사운드 에셋 미확보 상태이므로 **시스템 설계만** 진행. 에셋 확보 후 즉시 통합 가능한 구조.

### 2.1 아키텍처

ECS 이벤트 → MonoBehaviour AudioSource 풀 (Hybrid 브릿지)

### 2.2 ECS 컴포넌트

`Shared/Components/Sound/SoundType.cs` - SoundType enum만 정의 (Shared에서 참조 가능하도록)

`Client/Components/Sound/SoundEvent.cs`:
```csharp
public struct SoundEvent : IBufferElementData
{
    public SoundType Type;     // MeleeHit, RangedShot, UnitDeath 등
    public float3 Position;
    public float Volume;
}
```

`Client/Component/Singleton/SoundEventState.cs`: 사운드 이벤트 싱글톤 (DynamicBuffer<SoundEvent> 보유)

### 2.3 시스템

**클라이언트**: `Client/Systems/Sound/SoundEventEmitSystem.cs`
- 애니메이션 상태 변화 감지 → SoundEvent 버퍼에 추가
- 예: 공격 클립 전환 시 MeleeHit, 사망 시 Death

**MonoBehaviour**: `Client/Controller/Sound/SoundManager.cs`
- MinimapRenderer 패턴 참조 (World.All에서 클라이언트 월드 탐색)
- AudioSource 풀 (32개), 라운드로빈 재사용
- 카메라 거리 기반 컬링 (80m 밖 무시)
- 동일 SoundType 동시 재생 수 제한

### 2.4 기존 파일 수정

`ClientBootstrapSystem.cs`: SoundEventState 싱글톤 + SoundEvent 버퍼 생성 추가

---

## Part 3: SystemGroup 배치

```
[SimulationSystemGroup - Server]
  ... (기존 시스템들) ...
  [FixedStepSimulationSystemGroup]
    MeleeAttackSystem → RangedAttackSystem → DamageApplySystem
      ↓ UpdateAfter(typeof(FixedStepSimulationSystemGroup))
  VATAnimationStateUpdateSystem (유닛+적 애니메이션 상태 갱신)

[SimulationSystemGroup - Client]
  VATAnimationInitSystem (새 메시 엔티티 초기화)
    ↓ UpdateBefore
  VATAnimationPlaybackSystem (애니메이션 재생 → VATAnimParam 갱신)
    ↓ UpdateAfter
  SoundEventEmitSystem (상태 변화 → 사운드 이벤트)
    ↓ (기존)
  TeamColorSystem (팀 색상 - 기존 유지)
```

---

## Part 4: 전체 파일 목록

### 신규 생성
```
Assets/
├── Editor/VATBaker/
│   ├── VATBakerWindow.cs
│   └── VATBakeUtility.cs
├── Scripts/
│   ├── Shared/
│   │   ├── Components/Animation/VATAnimationState.cs
│   │   ├── Components/Animation/VATClipLibrary.cs
│   │   ├── Components/Sound/SoundType.cs
│   │   └── Animation/VATClipDataAsset.cs
│   ├── Client/
│   │   ├── Components/Animation/VATAnimParam.cs
│   │   ├── Components/Sound/SoundEvent.cs
│   │   ├── Component/Singleton/SoundEventState.cs
│   │   ├── Systems/Animation/VATAnimationInitSystem.cs
│   │   ├── Systems/Animation/VATAnimationPlaybackSystem.cs
│   │   ├── Systems/Sound/SoundEventEmitSystem.cs
│   │   └── Controller/Sound/SoundManager.cs
│   ├── Server/Systems/Animation/VATAnimationStateUpdateSystem.cs
│   └── Authoring/Animation/VATAnimationAuthoring.cs
├── Shaders/VATAnimation.shader
└── VATData/ (베이킹 출력 저장소)
```

### 기존 수정
| 파일 | 변경 |
|------|------|
| `ClientBootstrapSystem.cs` | SoundEventState 싱글톤 초기화 추가 |
| 유닛/적 프리팹 8개 | VATAnimationAuthoring 추가, 머티리얼 교체 |

---

## Part 5: 구현 순서

| Phase | 작업 | 전제 조건 |
|-------|------|----------|
| 1 | VAT 셰이더 + ECS 컴포넌트 정의 | 없음 |
| 2 | VAT 에디터 베이킹 툴 | 스켈레탈 FBX 모델 필요 |
| 3 | ECS 시스템 (서버 + 클라이언트) | Phase 1 |
| 4 | Authoring + 프리팹 통합 | Phase 2, 3 |
| 5 | 사운드 시스템 (설계 + 구조) | Phase 1 |
| 6 | 사운드 에셋 통합 | Phase 5 + 사운드 파일 |
| 7 | 문서 업데이트 | 전체 완료 후 |

---

## Part 6: 검증 방법

1. **VAT 베이킹**: Editor 툴에서 테스트 FBX → Position Texture + ClipData 생성 확인
2. **셰이더**: VAT 머티리얼 적용한 정적 메시에서 VATAnimParam 수동 변경 → 버텍스 변형 확인
3. **ECS 통합**: Hero 프리팹 1개로 파일럿 테스트 → Idle/Moving/Attacking 전환 시 애니메이션 재생 확인
4. **대규모 테스트**: 500+ 유닛 스폰 → 프로파일러에서 GPU/CPU 부하 확인
5. **네트워크**: 클라이언트-서버 모드에서 적 애니메이션 동기화 확인
6. **사운드**: SoundManager에 테스트 AudioClip 할당 → 전투 시 사운드 재생 확인

---

## Part 7: 주요 기술적 고려사항

- **UV2 인코딩**: SV_VertexID 대신 베이킹 시 UV2에 버텍스 인덱스를 인코딩 (Entities Graphics 호환성)
- **RGBAHalf vs RGBAFloat**: Half(16bit)로 시작, 정밀도 부족 시 Float(32bit)로 전환
- **프레임 보간**: 셰이더에서 2프레임 lerp로 끊김 방지
- **bool blittable**: VATClipInfo.Loop 필드는 BlobAsset 내부 읽기전용이므로 MarshalAs 불필요
- **TeamColorSystem 호환**: VATAnimParam과 URPMaterialPropertyBaseColor는 독립 초기화 (충돌 없음)
- **LOD**: Phase 1에서는 미구현, 프로파일링 후 필요 시 추가
- **Dying/Dead 상태 처리**: `ClientDeathSystem`이 사망 엔티티의 렌더링을 비활성화하므로, Dying 클립은 비활성화 전까지만 재생됨. VATAnimationStateUpdateSystem에서 Dying/Dead/Disabled 상태도 클립 인덱스에 매핑 필요 (해당 애니메이션 클립이 없으면 Idle로 폴백)

## 핵심 참조 파일
- `Client/Systems/TeamColorSystem.cs` - 자식 메시 엔티티 초기화 패턴 (Parent 체인 탐색 + ECB)
- `Shared/Components/State/UnitActionState.cs` - Action enum (Idle/Moving/Working/Attacking/Dying/Dead)
- `Shared/Components/State/EnemyState.cs` - EnemyContext enum (Idle/Wandering/Attacking/Chasing/Dormant/Dying/Dead)
- `Client/Systems/Initialize/ClientBootstrapSystem.cs` - 싱글톤 초기화 패턴
