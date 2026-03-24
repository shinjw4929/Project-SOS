# Phase 1: VAT 셰이더 + ECS 컴포넌트 정의

**전제 조건**: 없음

---

## URP 셰이더

**신규 파일**: `Assets/Shaders/VATAnimation.shader` (URP Lit 기반 HLSL)

### 셰이더 프로퍼티

| 프로퍼티 | 타입 | 용도 |
|---------|------|------|
| `_VATPositionTex` | Texture2D (RGBAHalf) | Position Texture (Phase 2에서 베이킹) |
| `_VATTexelSize` | float4 | 텍스처 크기 역수. `x = 1.0 / 텍스처너비(버텍스 수)`, `y = 1.0 / 텍스처높이(총 프레임)` |
| `_VATAnimParam` | float4 | **Per-entity** MaterialPropertyComponent (아래 VATAnimParam 컴포넌트에서 주입) |
| `_BaseColor` | float4 | 기존 TeamColorSystem 호환 유지 (URPMaterialPropertyBaseColor가 주입) |

### 핵심 버텍스 셰이더 로직

```hlsl
// _VATAnimParam 레이아웃:
//   x = normalizedTime (0~1, 클립 내 진행률)
//   y = clipStartRow (정규화된 V 좌표, startRow / textureHeight)
//   z = clipRowCount (정규화된 높이, rowCount / textureHeight)
//   w = reserved

// UV2에서 버텍스 인덱스 읽기 (SV_VertexID 대체 — Entities Graphics 호환)
float vertexIndex = v.texcoord1.x;  // UV2.x = 버텍스 인덱스 (0, 1, 2, ...)

// 텍셀 중심 샘플링: +0.5 오프셋으로 텍셀 경계 아티팩트 방지
float u = (vertexIndex + 0.5) * _VATTexelSize.x;

// 클립 내 프레임 위치 계산
float frameFloat = _VATAnimParam.x * (_VATAnimParam.z / _VATTexelSize.y);  // 정규화 진행률 → 프레임 수
float vBase = _VATAnimParam.y;  // 클립 시작 V 좌표

float vFloor = vBase + (floor(frameFloat) + 0.5) * _VATTexelSize.y;
float vCeil  = vBase + (ceil(frameFloat)  + 0.5) * _VATTexelSize.y;

// 프레임 간 선형 보간 (끊김 방지)
float3 posA = tex2Dlod(_VATPositionTex, float4(u, vFloor, 0, 0)).xyz;
float3 posB = tex2Dlod(_VATPositionTex, float4(u, vCeil, 0, 0)).xyz;
float3 animatedPos = lerp(posA, posB, frac(frameFloat));
```

### 호환성

- `_BaseColor` 프로퍼티 유지 → TeamColorSystem이 URPMaterialPropertyBaseColor로 주입하는 값과 곱산
- Normal은 VAT에서 별도 처리하지 않음 (Phase 1 범위 외, 필요 시 Normal Texture 추가)

---

## ECS 컴포넌트

### VATAnimationState (Shared)

**파일**: `Assets/Scripts/Shared/Components/Animation/VATAnimationState.cs`

```csharp
[GhostComponent]
public struct VATAnimationState : IComponentData
{
    [GhostField] public byte CurrentClipIndex;   // 클립 인덱스 (유닛: 0-6, 적: 0-5)
    [GhostField(Quantization = 100)] public float AnimStartTime;  // 클립 전환 시점의 ElapsedTime (0.01초 정밀도)
}
```

서버가 UnitActionState/EnemyState 변화 시 갱신, Ghost로 클라이언트에 동기화 (~3바이트/엔티티).

### VATClipLibrary (Shared)

**파일**: `Assets/Scripts/Shared/Components/Animation/VATClipLibrary.cs`

```csharp
public struct VATClipInfo
{
    public int StartRow;      // 텍스처 내 시작 행
    public int RowCount;      // 클립 프레임 수
    public float Fps;         // 재생 속도
    public bool Loop;         // BlobAsset 내부 읽기전용 → MarshalAs 불필요
}

public struct VATClipBlobData
{
    public BlobArray<VATClipInfo> Clips;  // 인덱스 = CurrentClipIndex
    public int TextureHeight;             // 총 텍스처 높이 (정규화 계산용)
}

public struct VATClipLibrary : IComponentData
{
    public BlobAssetReference<VATClipBlobData> Value;
}
```

### VATClipDataAsset (Shared)

**파일**: `Assets/Scripts/Shared/Components/Animation/VATClipDataAsset.cs`

```csharp
[CreateAssetMenu(fileName = "VATClipData", menuName = "VAT/Clip Data")]
public class VATClipDataAsset : ScriptableObject
{
    public Texture2D PositionTexture;  // RGBAHalf Position Texture
    public Mesh StaticMesh;            // 바인드포즈 정적 메시

    [System.Serializable]
    public struct ClipEntry
    {
        public string Name;       // 클립 이름 (에디터 표시용)
        public int StartRow;
        public int RowCount;
        public float Fps;
        public bool Loop;
    }

    public ClipEntry[] Clips;
}
```

Phase 2 베이킹 툴이 이 에셋을 자동 생성. Phase 4 Authoring에서 BlobAssetReference로 변환.

### VATAnimParam (Client)

**파일**: `Assets/Scripts/Client/Component/Animation/VATAnimParam.cs`

```csharp
[MaterialProperty("_VATAnimParam")]
public struct VATAnimParam : IComponentData
{
    public float4 Value;
    // x = normalizedTime, y = clipStartRow(정규화), z = clipRowCount(정규화), w = reserved
}
```

TeamColorSystem의 URPMaterialPropertyBaseColor와 동일 패턴. Entities Graphics가 매 프레임 GPU에 업로드.

### VATAnimTarget (Client)

**파일**: `Assets/Scripts/Client/Component/Animation/VATAnimTarget.cs`

```csharp
public struct VATAnimTarget : IComponentData
{
    public Entity RootEntity;  // 메시 엔티티 → 루트 엔티티(VATAnimationState 보유) 참조
}
```

TeamColorTarget과 동일 패턴. VATAnimationInitSystem에서 Parent 체인 탐색 후 ECB로 부착.

### PreviousClipIndex (Client)

**파일**: `Assets/Scripts/Client/Component/Animation/PreviousClipIndex.cs`

```csharp
public struct PreviousClipIndex : IComponentData
{
    public byte Value;
}
```

SoundEventEmitSystem(Phase 5)에서 상태 변화 감지용. VATAnimationInitSystem에서 VATAnimParam과 함께 부착.

---

## 체크리스트

- [ ] URP Lit 기반 `VATAnimation.shader` 작성
- [ ] `_VATPositionTex`, `_VATTexelSize` 셰이더 프로퍼티 선언
- [ ] `_VATAnimParam` float4 per-entity 프로퍼티 구현
- [ ] UV2.x에서 버텍스 인덱스 읽기 로직
- [ ] 텍셀 중심 샘플링 (+0.5 오프셋)
- [ ] 프레임 간 보간 (floor/ceil + lerp) 구현
- [ ] `_BaseColor` 프로퍼티 유지 (TeamColorSystem 호환)
- [ ] `VATAnimationState` 컴포넌트 (Shared, GhostField)
- [ ] `VATClipBlobData` + `VATClipInfo` BlobAsset 구조 정의
- [ ] `VATClipLibrary` 컴포넌트 (Shared, BlobAssetReference)
- [ ] `VATClipDataAsset` ScriptableObject (Shared, CreateAssetMenu)
- [ ] `VATAnimParam` MaterialProperty 컴포넌트 (Client)
- [ ] `VATAnimTarget` 컴포넌트 (Client, 메시→루트 참조)
- [ ] `PreviousClipIndex` 컴포넌트 (Client, 사운드 변화 감지용)
