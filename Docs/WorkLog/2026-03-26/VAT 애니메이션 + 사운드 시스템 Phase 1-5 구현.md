# VAT 애니메이션 + 사운드 시스템 Phase 1-5 구현

**날짜**: 2026-03-26
**작업**: GPU 애니메이션(VAT) 시스템과 사운드 이벤트 시스템의 코드 구조 전체 구현 (Phase 1~5)

---

## 변경 파일 목록

### 신규 파일

| 파일 | 설명 |
|------|------|
| `Assets/Shaders/VATAnimation.shader` | URP Lit 기반 VAT 셰이더 (ForwardLit + ShadowCaster + DepthOnly, DOTS Instancing 지원) |
| `Assets/Scripts/Shared/Components/Animation/VATAnimationState.cs` | Ghost 동기화 애니메이션 상태 (CurrentClipIndex + AnimStartTime) |
| `Assets/Scripts/Shared/Components/Animation/VATClipLibrary.cs` | VATClipInfo + VATClipBlobData + VATClipLibrary BlobAsset 구조 |
| `Assets/Scripts/Shared/Components/Animation/VATClipDataAsset.cs` | 베이킹 출력물 ScriptableObject (PositionTexture + StaticMesh + ClipEntry[]) |
| `Assets/Scripts/Client/Component/Animation/VATAnimParam.cs` | MaterialProperty("_VATAnimParam") per-entity 셰이더 파라미터 |
| `Assets/Scripts/Client/Component/Animation/VATAnimTarget.cs` | 메시->루트 엔티티 참조 (TeamColorTarget 패턴) |
| `Assets/Scripts/Client/Component/Animation/PreviousClipIndex.cs` | VAT 클립 전환 감지용 |
| `Assets/Editor/VATBaker/VATBakeUtility.cs` | VAT 베이킹 로직 (AnimationMode 샘플링, RGBAHalf 텍스처, UV2 인코딩) |
| `Assets/Editor/VATBaker/VATBakerWindow.cs` | EditorWindow UI (FBX 슬롯, 클립 편집, 진행률 표시) |
| `Assets/Scripts/Server/Systems/Animation/VATAnimationStateUpdateSystem.cs` | 서버: UnitActionState/EnemyState -> 클립 인덱스 갱신 |
| `Assets/Scripts/Client/Systems/Animation/VATAnimationInitSystem.cs` | 클라이언트: 새 메시 엔티티 초기화 (Parent 체인 탐색 패턴) |
| `Assets/Scripts/Client/Systems/Animation/VATAnimationPlaybackSystem.cs` | 클라이언트: VATAnimParam 계산 IJobEntity |
| `Assets/Scripts/Client/Systems/Animation/CombatTiltSystem.cs` | 클라이언트: 전투 기울임 (전체 유닛/적 대상, VAT 무관) |
| `Assets/Scripts/Authoring/Animation/VATAnimationAuthoring.cs` | Authoring Baker (VATClipDataAsset -> BlobAsset 변환) |
| `Assets/Scripts/Shared/Components/Sound/SoundType.cs` | 사운드 타입 enum (MeleeHit, RangedShot 등) |
| `Assets/Scripts/Client/Component/Sound/SoundEvent.cs` | IBufferElementData 사운드 이벤트 |
| `Assets/Scripts/Client/Component/Singleton/SoundEventState.cs` | 싱글톤 마커 컴포넌트 |
| `Assets/Scripts/Client/Component/State/PreviousActionState.cs` | 유닛 상태 변화 감지용 |
| `Assets/Scripts/Client/Component/State/PreviousEnemyContext.cs` | 적 상태 변화 감지용 |
| `Assets/Scripts/Client/Systems/Sound/SoundEventEmitSystem.cs` | ActionState/EnemyState 변화 -> SoundEvent 생성 |
| `Assets/Scripts/Client/Controller/Sound/SoundManager.cs` | AudioSource 풀, 3D 오디오, 거리 컬링 |

### 수정 파일

| 파일 | 변경 내용 |
|------|----------|
| `Assets/Scripts/Shared/Singletons/GameSettings.cs` | CombatTiltAngle, CombatTiltSpeed 필드 추가 |
| `Assets/Scripts/Authoring/Settings/GameSettingsAuthoring.cs` | Animation 헤더 + combatTiltAngle/combatTiltSpeed 필드 + Baker 매핑 |
| `Assets/Scripts/Client/Systems/Initialize/ClientBootstrapSystem.cs` | SoundEventState 싱글톤 + SoundEvent 버퍼 생성 추가 |

---

## 아키텍처 요약

### VAT 파이프라인
1. **에디터**: VATBakerWindow에서 FBX -> Position Texture(RGBAHalf) + Static Mesh(UV2 인코딩) + ClipData + Material 베이킹
2. **서버**: VATAnimationStateUpdateSystem이 Action/EnemyState 변화 -> CurrentClipIndex 갱신 (Ghost 동기화)
3. **클라이언트**: VATAnimationPlaybackSystem이 BlobAsset 기반 normalizedTime/startRow/rowCount 계산 -> 셰이더에 전달
4. **셰이더**: Position Texture에서 2프레임 샘플링 + lerp 보간으로 GPU 애니메이션

### 사운드 파이프라인
1. SoundEventEmitSystem: UnitActionState/EnemyState 변화 감지 -> SoundEvent 버퍼에 이벤트 추가
2. SoundManager (MonoBehaviour): 버퍼 소비 -> AudioSource 풀로 3D 사운드 재생 (거리 컬링 + 동시 재생 제한)

---

## 후속 작업 (Phase 6)

- [ ] VATBakerWindow에서 Hero(LittleSquirrel), EnemySmall/EnemyFlying(Ghost) FBX 베이킹 실행
- [ ] 프리팹 3개에 VATAnimationAuthoring 추가 + 메시/머티리얼 교체
- [ ] SoundManager에 AudioClip 할당
- [ ] 통합 테스트 + 성능 프로파일링
- [ ] 문서 업데이트 (Architecture.md, 코드베이스 구조.md)
