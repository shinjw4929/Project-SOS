# Phase 1: VAT 셰이더 + ECS 컴포넌트 정의

**전제 조건**: 없음

---

## URP 셰이더

**신규 파일**: `Assets/Shaders/VATAnimation.shader` (URP Lit 기반 HLSL)

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

---

## ECS 컴포넌트

### Shared 어셈블리

| 파일 | 컴포넌트 | 역할 |
|------|---------|------|
| `Shared/Components/Animation/VATAnimationState.cs` | `VATAnimationState` | **유닛/적 공통** Ghost 동기화. `byte CurrentClipIndex` + `float AnimStartTime` |
| `Shared/Components/Animation/VATClipLibrary.cs` | `VATClipLibrary` | BlobAssetReference로 클립 메타데이터 참조 |
| `Shared/Animation/VATClipDataAsset.cs` | ScriptableObject | 에디터에서 클립 정보 + 텍스처 참조 관리 |

### Client 어셈블리

| 파일 | 컴포넌트 | 역할 |
|------|---------|------|
| `Client/Components/Animation/VATAnimParam.cs` | `VATAnimParam` | `[MaterialProperty("_VATAnimParam")]` per-entity 셰이더 파라미터 |

---

## 체크리스트

- [ ] URP Lit 기반 `VATAnimation.shader` 작성
- [ ] `_VATAnimParam` float4 per-entity 프로퍼티 구현
- [ ] UV2 채널 버텍스 인덱스 인코딩 로직
- [ ] 프레임 간 보간 (lerp) 구현
- [ ] `_BaseColor` 프로퍼티 유지 (TeamColorSystem 호환)
- [ ] `VATAnimationState` 컴포넌트 (Shared, Ghost 동기화용)
- [ ] `VATClipLibrary` 컴포넌트 (Shared, BlobAssetReference)
- [ ] `VATClipDataAsset` ScriptableObject (Shared)
- [ ] `VATAnimParam` MaterialProperty 컴포넌트 (Client)
