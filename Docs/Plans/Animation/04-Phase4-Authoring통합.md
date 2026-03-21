# Phase 4: Authoring + 프리팹 통합

**전제 조건**: Phase 2, Phase 3

---

## Authoring

**신규 파일**: `Authoring/Animation/VATAnimationAuthoring.cs`

- VATClipDataAsset 참조 → BlobAssetReference<VATClipBlobData> 생성
- VATAnimationState + VATClipLibrary 부착 (유닛/적 공통, 구분 로직 불필요)
- **기존 UnitAuthoring/EnemyAuthoring 수정 불필요** (Composition 패턴)

---

## 프리팹 변경

```
변경 전:                                변경 후:
Hero (Root)                             Hero (Root)
├── *Authoring 컴포넌트들               ├── *Authoring + VATAnimationAuthoring
└── Child (MeshFilter + MeshRenderer)   └── Child (MeshFilter(VAT 메시) + MeshRenderer(VAT 머티리얼))
```

대상 프리팹: 유닛/적 프리팹 8개 (Hero, Worker, Striker, Archer, Tank, EnemySmall, EnemyBig, EnemyFlying)

---

## 체크리스트

- [ ] `VATAnimationAuthoring.cs` 구현 (BlobAssetReference<VATClipBlobData> 생성)
- [ ] VATAnimationState + VATClipLibrary 부착 확인
- [ ] 유닛/적 프리팹 8개에 VATAnimationAuthoring 추가
- [ ] 프리팹 MeshFilter → VAT 정적 메시로 교체
- [ ] 프리팹 MeshRenderer 머티리얼 → VAT 머티리얼로 교체
- [ ] 기존 UnitAuthoring/EnemyAuthoring 수정 불필요 확인 (Composition 패턴)
