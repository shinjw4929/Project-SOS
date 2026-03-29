# Phase 4: Authoring + 프리팹 통합

**전제 조건**: Phase 2, Phase 3

---

## Authoring

**신규 파일**: `Assets/Scripts/Authoring/Animation/VATAnimationAuthoring.cs`

### Baker 구현 패턴

```csharp
public class VATAnimationAuthoring : MonoBehaviour
{
    public VATClipDataAsset ClipData;  // Inspector에서 Phase 2 출력물 할당

    public class Baker : Baker<VATAnimationAuthoring>
    {
        public override void Bake(VATAnimationAuthoring authoring)
    {
        if (authoring.ClipData == null)
        {
            Debug.LogError($"[VAT] VATAnimationAuthoring on '{authoring.gameObject.name}' " +
                "has no ClipData assigned. Animation will not play.", authoring.gameObject);
            return;
        }

        var entity = GetEntity(TransformUsageFlags.Dynamic);

        // 1. VATAnimationState 초기화 (서버에서 갱신, Ghost 동기화)
        AddComponent(entity, new VATAnimationState
        {
            CurrentClipIndex = 0,  // Idle
            AnimStartTime = 0
        });

        // 2. BlobAssetReference 생성 (ScriptableObject → BlobAsset 변환)
        var builder = new BlobBuilder(Allocator.Temp);
        ref var root = ref builder.ConstructRoot<VATClipBlobData>();
        root.TextureHeight = authoring.ClipData.PositionTexture.height;

        var clips = builder.Allocate(ref root.Clips, authoring.ClipData.Clips.Length);
        for (int i = 0; i < authoring.ClipData.Clips.Length; i++)
        {
            var src = authoring.ClipData.Clips[i];
            clips[i] = new VATClipInfo
            {
                StartRow = src.StartRow,
                RowCount = src.RowCount,
                Fps = src.Fps,
                Loop = src.Loop
            };
        }

        var blobRef = builder.CreateBlobAssetReference<VATClipBlobData>(Allocator.Persistent);
        AddBlobAsset(ref blobRef, out _);
        builder.Dispose();

        AddComponent(entity, new VATClipLibrary { Value = blobRef });
    }
    }
}
```

- **기존 UnitAuthoring/EnemyAuthoring 수정 불필요** (Composition 패턴: 같은 GameObject에 VATAnimationAuthoring을 추가)

---

## 프리팹 변경

### 변경 구조

```
변경 전:                                변경 후:
Hero (Root)                             Hero (Root)
├── *Authoring 컴포넌트들               ├── *Authoring + VATAnimationAuthoring
└── Child (MeshFilter + MeshRenderer)   └── Child (MeshFilter(VAT 메시) + MeshRenderer(VAT 머티리얼))
```

### VAT 메시 / 머티리얼 출처

- **VAT 메시**: Phase 2 베이킹 출력물 (`Assets/VATData/{모델명}/{모델명}_BindPose.asset`)
  - 바인드포즈 정적 메시 + UV2 버텍스 인덱스 인코딩 완료
- **VAT 머티리얼**: Phase 2 VATBakeUtility가 자동 생성 (`Assets/VATData/{모델명}/{모델명}_VAT.mat`)
  - `_VATPositionTex`, `_VATTexelSize` 자동 설정 완료
  - 모델별 머티리얼이 이미 존재하므로 Phase 4에서는 프리팹에 할당만 수행

### VAT 적용 대상 프리팹 (3개)

에셋 조사 결과([00-에셋조사.md](00-에셋조사.md))에 따라, 스켈레탈 애니메이션 + 걷기 클립이 있는 프리팹만 VAT 적용.

| 프리팹 | 원본 에셋 | VATClipDataAsset | 베이킹 클립 |
|--------|----------|-----------------|------------|
| Hero | LittleSquirrel (Humanoid) | `Assets/VATData/Hero/Hero_ClipData.asset` | Idle, Walk, Eat |
| EnemySmall | Ghost (Generic) | `Assets/VATData/EnemySmall/EnemySmall_ClipData.asset` | ghost_idle, ghost_run, ghost_attack, ghost_dissolve |
| EnemyFlying | Ghost (Generic) | `Assets/VATData/EnemyFlying/EnemyFlying_ClipData.asset` | EnemySmall과 동일 (같은 FBX) |

### VAT 미적용 프리팹 (5개) — 변경 없음

| 프리팹 | 원본 에셋 | 미적용 사유 |
|--------|----------|-----------|
| Worker | Origami Rabbit | 리깅 없음 (정적 메시) |
| Striker | Origami Ox | 리깅 없음 (정적 메시) |
| Tank | Origami Dragon | 리깅 없음 (정적 메시) |
| Archer | Origami Crane | 리깅 없음 (정적 메시) |
| EnemyBig | Monster Ghost | 걷기 클립 없음 (idle_up_down만) |

이 5종은 기존 정적 메시/머티리얼을 유지하며, 전투 기울임(CombatTiltSystem)만 적용된다.

---

## 체크리스트

- [x] Phase 2 출력물 존재 확인 (`Assets/VATData/{모델명}/` 내 4개: `*_ClipData.asset`, `*_Positions.asset`, `*_BindPose.asset`, `*_VAT.mat`)
- [x] `VATAnimationAuthoring.cs` 구현 (MonoBehaviour + Baker)
- [x] Baker: ClipData null 시 `Debug.LogError` 진단 + 조기 반환
- [x] Baker: VATAnimationState 초기화 (CurrentClipIndex=0, AnimStartTime=0)
- [x] Baker: BlobBuilder로 VATClipBlobData 생성 (ScriptableObject → BlobAsset)
- [x] Baker: VATClipLibrary 컴포넌트 부착
- [x] Phase 2에서 자동 생성된 VAT 머티리얼(`*_VAT.mat`)의 `_VATPositionTex`, `_VATTexelSize` 확인
- [x] VAT 적용 프리팹 **3개**에 VATAnimationAuthoring 추가: Hero, EnemySmall, EnemyFlying
- [x] 각 프리팹의 Inspector에서 ClipData 할당 확인
- [x] 프리팹 MeshFilter → VAT 정적 메시(`_BindPose.asset`)로 교체 (3개만)
- [x] 프리팹 MeshRenderer 머티리얼 → VAT 머티리얼로 교체 (3개만)
- [x] VAT 미적용 프리팹 5개는 변경하지 않음 확인
- [x] 기존 UnitAuthoring/EnemyAuthoring 수정 불필요 확인 (Composition 패턴)
- [x] Bake 실행 후 에러 메시지 없음 확인
