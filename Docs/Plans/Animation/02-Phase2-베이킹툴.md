# Phase 2: VAT 에디터 베이킹 툴

**전제 조건**: Phase 1 (VATClipDataAsset 클래스 정의) + 스켈레탈 FBX 모델

---

## 오프라인 베이킹 파이프라인

**입력**: SkinnedMeshRenderer + AnimationClip이 있는 FBX 모델
**출력**:
- Position Texture (RGBAHalf): 행=프레임, 열=버텍스, 값=XYZ 좌표
- VATClipDataAsset (ScriptableObject): 클립별 메타데이터
- Static Mesh: 바인드포즈 정적 메시 (런타임 MeshFilter용)

### 베이킹 알고리즘

```
1. FBX에서 SkinnedMeshRenderer + AnimationClip[] 추출
2. 버텍스 수 확인 → 텍스처 너비 결정
   ※ 텍스처 크기 한계: 너비/높이 4096 초과 시 경고 → 모델 LOD 또는 텍스처 분할 필요
3. 각 클립 순회:
   a. 프레임 수 = round(clipLength × fps)  ← 정확한 프레임 수 산출
   b. 전체 프레임 수 누적 → 텍스처 높이 결정
4. Texture2D(너비=버텍스수, 높이=총프레임, RGBAHalf) 생성
5. AnimationMode로 샘플링 (Editor 전용 API):
   a. AnimationMode.StartAnimationMode()
   b. 각 클립/각 프레임 순회:
      - AnimationMode.BeginSampling()
      - AnimationClip.SampleAnimation(gameObject, time)
      - AnimationMode.EndSampling()
      - SkinnedMeshRenderer.BakeMesh(tempMesh, useScale: false)
        → useScale: false 필수 (로컬 좌표 유지)
      - 각 버텍스의 로컬 좌표를 텍스처 행에 기록
   c. AnimationMode.StopAnimationMode()
6. Static Mesh: 바인드포즈(프레임 0) 메시를 별도 에셋으로 저장
7. UV2 인코딩: Static Mesh의 UV2.x에 버텍스 인덱스(0, 1, 2, ...) 기록
   ※ 정규화 금지! uv2[i] = new Vector2((float)i, 0)  ← 정수값 그대로 기록
8. VATClipDataAsset 생성 (클립별 메타데이터 + 텍스처/메시 참조)
9. 모든 에셋을 지정 경로에 저장
```

### 텍스처 픽셀 레이아웃

```
텍스처 포맷: RGBAHalf (16bit per channel)
채널 매핑: R=X좌표, G=Y좌표, B=Z좌표, A=미사용(1.0)
좌표 공간: 오브젝트 로컬 좌표 (정규화하지 않음)

텍스처 레이아웃 (예: 버텍스 500개, 총 120프레임):
너비 = 500 (버텍스 수)
높이 = 120 (총 프레임)
[Idle:   행 0-29]   30fps, loop
[Walk:   행 30-59]  30fps, loop
[Attack: 행 60-89]  30fps, no loop
[Work:   행 90-119] 30fps, loop
```

### RGBAHalf 텍스처 쓰기

RGBAHalf 포맷은 `SetPixels()`을 사용할 수 없다. `GetRawTextureData<half4>()`로 NativeArray에 직접 쓴다:

```csharp
var texture = new Texture2D(width, height, TextureFormat.RGBAHalf, false);
var data = texture.GetRawTextureData<half4>();

for (int row = 0; row < height; row++)
{
    for (int col = 0; col < width; col++)
    {
        int pixelIndex = row * width + col;
        data[pixelIndex] = new half4(
            new half(positions[col].x),
            new half(positions[col].y),
            new half(positions[col].z),
            new half(1.0f));
    }
}

texture.Apply();
```

### UV2 인코딩

셰이더에서 SV_VertexID 대신 UV2를 사용 (Entities Graphics 호환성).

```
UV2.x = 버텍스 인덱스 (float, 정수값: 0.0, 1.0, 2.0, ...)
UV2.y = 미사용 (0.0)
```

Static Mesh 생성 시 UV2 채널에 인덱스를 기록. 셰이더에서 `v.texcoord1.x`로 읽음.

### 파일 저장 경로 규약

```
Assets/VATData/{모델명}/
├── {모델명}_Positions.asset     # Position Texture (RGBAHalf)
├── {모델명}_BindPose.asset      # Static Mesh (바인드포즈 + UV2 인코딩)
├── {모델명}_ClipData.asset      # VATClipDataAsset (ScriptableObject)
└── {모델명}_VAT.mat             # VAT Material (셰이더 + 텍스처 + TexelSize 자동 설정)
```

### 생성 파일

- `Assets/Editor/VATBaker/VATBakerWindow.cs` - EditorWindow UI (FBX 슬롯, 클립 선택, 출력 경로, Bake 버튼)
- `Assets/Editor/VATBaker/VATBakeUtility.cs` - 베이킹 로직 (위 알고리즘 구현)

### VAT 머티리얼 자동 생성

VATBakeUtility가 베이킹 완료 시 VAT Material도 함께 생성한다. Phase 4에서 수동 생성/설정할 필요가 없다:

```csharp
// 9. Material 생성 (VATAnimation.shader 기반)
var shader = Shader.Find("Custom/VATAnimation");  // Phase 1에서 생성한 셰이더
var material = new Material(shader);
material.SetTexture("_VATPositionTex", positionTexture);
material.SetVector("_VATTexelSize", new Vector4(
    1.0f / positionTexture.width,   // x: 1/버텍스수
    1.0f / positionTexture.height,  // y: 1/총프레임수
    0, 0));
AssetDatabase.CreateAsset(material, $"{outputPath}/{modelName}_VAT.mat");
```

### EditorWindow 요구사항

- **FBX 슬롯**: `ObjectField`로 FBX 프리팹 직접 드래그 지원
- **클립 자동 추출**: FBX 로드 후 AnimationClip[] 자동 추출 → 리스트 표시
- **클립별 설정**: 각 클립의 Fps(spinbox, 기본값=클립 샘플레이트 자동 감지), Loop(toggle) 편집 가능
- **진행 표시**: `EditorUtility.DisplayProgressBar()`로 프레임 베이킹 진행률 표시
- **에러 핸들링**:
  - 텍스처 크기 > 4096 초과 시 경고
  - SkinnedMeshRenderer 미발견 시 에러
  - 복수 SkinnedMeshRenderer: 첫 번째만 사용 (경고 표시)
  - 폴더 자동 생성: `AssetDatabase.CreateFolder()`로 출력 경로 보장

---

## 체크리스트

- [ ] `VATBakerWindow.cs` EditorWindow UI 구현 (FBX 슬롯, 클립 자동 추출, Fps/Loop 편집, 출력 경로)
- [ ] `VATBakeUtility.cs` 베이킹 로직 구현
- [ ] FBX에서 SkinnedMeshRenderer + AnimationClip 추출
- [ ] 각 클립별 프레임 수 산출: `round(clipLength × fps)`
- [ ] AnimationMode 기반 샘플링 (StartAnimationMode → SampleAnimation → BakeMesh → StopAnimationMode)
- [ ] BakeMesh 호출 시 `useScale: false` (로컬 좌표 유지)
- [ ] Position Texture (RGBAHalf) 생성 — `GetRawTextureData<half4>()`로 NativeArray 직접 쓰기
- [ ] R=X, G=Y, B=Z, 행=프레임, 열=버텍스 레이아웃 확인
- [ ] Static Mesh 추출 (바인드포즈, 프레임 0)
- [ ] UV2 채널에 버텍스 인덱스 인코딩 (정규화 금지: `uv2[i] = new Vector2((float)i, 0)`)
- [ ] VATClipDataAsset 자동 생성 (클립별 StartRow, RowCount, Fps, Loop + 텍스처/메시 참조)
- [ ] VAT Material 자동 생성 (`_VATPositionTex` + `_VATTexelSize` 자동 설정)
- [ ] 파일 저장 경로 규약 적용 (`Assets/VATData/{모델명}/` — 4개 에셋)
- [ ] 텍스처 크기 한계(4096) 초과 시 경고 표시
- [ ] EditorUtility.DisplayProgressBar 진행률 표시
- [ ] 에러 핸들링: SMR 미발견, 복수 SMR 경고, 폴더 자동 생성
