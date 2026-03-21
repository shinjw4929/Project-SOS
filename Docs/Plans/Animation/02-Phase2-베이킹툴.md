# Phase 2: VAT 에디터 베이킹 툴

**전제 조건**: 스켈레탈 FBX 모델 필요

---

## 오프라인 베이킹 파이프라인

**입력**: SkinnedMeshRenderer + AnimationClip이 있는 FBX 모델
**출력**:
- Position Texture (RGBAHalf): 행=프레임, 열=버텍스, 값=XYZ 좌표
- VATClipDataAsset (ScriptableObject): 클립별 메타데이터
- Static Mesh: 바인드포즈 정적 메시 (런타임 MeshFilter용)

### 텍스처 레이아웃

```
텍스처 레이아웃 (예: 버텍스 500개, 총 120프레임):
너비 = 500 (버텍스 수)
높이 = 120 (총 프레임)
[Idle:   행 0-29]   30fps, loop
[Walk:   행 30-59]  30fps, loop
[Attack: 행 60-89]  30fps, no loop
[Work:   행 90-119] 30fps, loop
```

### 생성 파일

- `Assets/Editor/VATBaker/VATBakerWindow.cs` - EditorWindow UI
- `Assets/Editor/VATBaker/VATBakeUtility.cs` - 베이킹 로직 (AnimationClip.SampleAnimation → BakeMesh → Texture2D)

---

## 체크리스트

- [ ] `VATBakerWindow.cs` EditorWindow UI 구현
- [ ] `VATBakeUtility.cs` 베이킹 로직 구현
- [ ] Position Texture (RGBAHalf) 생성 — 행=프레임, 열=버텍스
- [ ] VATClipDataAsset 자동 생성 (클립별 startRow, rowCount, fps, loop)
- [ ] Static Mesh (바인드포즈) 추출 및 저장
- [ ] UV2 채널에 버텍스 인덱스 인코딩 (메시 베이킹 시)
