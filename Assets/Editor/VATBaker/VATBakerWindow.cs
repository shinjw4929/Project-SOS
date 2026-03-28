using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace VATBaker
{
    public class VATBakerWindow : EditorWindow
    {
        private GameObject fbxPrefab;
        private string modelName = "";
        private AnimationClip[] extractedClips;
        private List<ClipSetting> clipSettings = new List<ClipSetting>();
        private Vector2 scrollPos;

        private struct ClipSetting
        {
            public bool Enabled;
            public float Fps;
            public bool Loop;
        }

        [MenuItem("Tools/VAT Baker")]
        public static void ShowWindow()
        {
            GetWindow<VATBakerWindow>("VAT Baker");
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("VAT (Vertex Animation Texture) Baker", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            // FBX 슬롯
            EditorGUI.BeginChangeCheck();
            fbxPrefab = (GameObject)EditorGUILayout.ObjectField("FBX Prefab", fbxPrefab, typeof(GameObject), false);
            if (EditorGUI.EndChangeCheck() && fbxPrefab != null)
            {
                OnFBXChanged();
            }

            if (fbxPrefab == null)
            {
                EditorGUILayout.HelpBox("스켈레탈 애니메이션이 포함된 FBX 프리팹을 드래그하세요.", MessageType.Info);
                return;
            }

            // 모델 이름
            modelName = EditorGUILayout.TextField("Model Name", modelName);

            EditorGUILayout.Space(8);

            if (extractedClips == null || extractedClips.Length == 0)
            {
                EditorGUILayout.HelpBox("AnimationClip을 찾을 수 없습니다. FBX에 애니메이션이 포함되어 있는지 확인하세요.", MessageType.Warning);
                return;
            }

            // 클립 리스트
            EditorGUILayout.LabelField($"클립 목록 ({extractedClips.Length}개)", EditorStyles.boldLabel);
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.MaxHeight(300));

            int swapA = -1, swapB = -1;
            for (int i = 0; i < extractedClips.Length; i++)
            {
                var clip = extractedClips[i];
                var setting = clipSettings[i];

                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

                setting.Enabled = EditorGUILayout.Toggle(setting.Enabled, GUILayout.Width(20));
                EditorGUILayout.LabelField(clip.name, GUILayout.MinWidth(120));
                EditorGUILayout.LabelField($"{clip.length:F2}s", GUILayout.Width(50));

                EditorGUILayout.LabelField("FPS", GUILayout.Width(28));
                setting.Fps = EditorGUILayout.FloatField(setting.Fps, GUILayout.Width(50));
                setting.Fps = Mathf.Max(1f, setting.Fps);

                setting.Loop = EditorGUILayout.ToggleLeft("Loop", setting.Loop, GUILayout.Width(50));

                GUI.enabled = i > 0;
                if (GUILayout.Button("\u25b2", GUILayout.Width(22))) { swapA = i; swapB = i - 1; }
                GUI.enabled = i < extractedClips.Length - 1;
                if (GUILayout.Button("\u25bc", GUILayout.Width(22))) { swapA = i; swapB = i + 1; }
                GUI.enabled = true;

                EditorGUILayout.EndHorizontal();
                clipSettings[i] = setting;
            }

            if (swapA >= 0)
            {
                (extractedClips[swapA], extractedClips[swapB]) = (extractedClips[swapB], extractedClips[swapA]);
                (clipSettings[swapA], clipSettings[swapB]) = (clipSettings[swapB], clipSettings[swapA]);
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(8);

            // 프레임 요약 정보
            int enabledCount = 0;
            int totalFrames = 0;
            int vertexCount = 0;
            for (int i = 0; i < clipSettings.Count; i++)
            {
                if (!clipSettings[i].Enabled) continue;
                enabledCount++;
                totalFrames += Mathf.Max(1, Mathf.RoundToInt(extractedClips[i].length * clipSettings[i].Fps));
            }

            if (fbxPrefab != null)
            {
                var smr = fbxPrefab.GetComponentInChildren<SkinnedMeshRenderer>();
                if (smr != null)
                    vertexCount = smr.sharedMesh.vertexCount;
            }

            EditorGUILayout.HelpBox(
                $"활성 클립: {enabledCount}개 | 총 프레임: {totalFrames} | 버텍스: {vertexCount}\n" +
                $"텍스처 크기: {vertexCount} x {totalFrames} (RGBAHalf)\n" +
                $"출력 경로: Assets/VATData/{modelName}/",
                MessageType.None);

            if (vertexCount > 4096 || totalFrames > 4096)
                EditorGUILayout.HelpBox("텍스처 크기가 4096을 초과합니다!", MessageType.Warning);

            EditorGUILayout.Space(4);

            // Bake 버튼
            GUI.enabled = enabledCount > 0 && !string.IsNullOrEmpty(modelName);
            if (GUILayout.Button("Bake VAT", GUILayout.Height(32)))
            {
                ExecuteBake();
            }
            GUI.enabled = true;
        }

        private void OnFBXChanged()
        {
            extractedClips = VATBakeUtility.ExtractClips(fbxPrefab);
            clipSettings.Clear();

            if (string.IsNullOrEmpty(modelName))
                modelName = fbxPrefab.name;

            for (int i = 0; i < extractedClips.Length; i++)
            {
                var clip = extractedClips[i];
                clipSettings.Add(new ClipSetting
                {
                    Enabled = true,
                    Fps = clip.frameRate > 0 ? clip.frameRate : 30f,
                    Loop = clip.isLooping
                });
            }
        }

        private void ExecuteBake()
        {
            var settings = new List<VATBakeUtility.ClipSetting>();
            for (int i = 0; i < clipSettings.Count; i++)
            {
                if (!clipSettings[i].Enabled) continue;
                settings.Add(new VATBakeUtility.ClipSetting
                {
                    Clip = extractedClips[i],
                    Fps = clipSettings[i].Fps,
                    Loop = clipSettings[i].Loop
                });
            }

            try
            {
                var result = VATBakeUtility.Bake(fbxPrefab, settings.ToArray(), modelName);
                EditorUtility.DisplayDialog("VAT Baker", "베이킹 완료!", "OK");

                // 생성된 ClipData 에셋 선택
                if (result.ClipDataAsset != null)
                    Selection.activeObject = result.ClipDataAsset;
            }
            catch (System.OperationCanceledException)
            {
                Debug.Log("[VATBaker] 베이킹이 취소되었습니다.");
            }
            catch (System.Exception e)
            {
                EditorUtility.DisplayDialog("VAT Baker Error", e.Message, "OK");
                Debug.LogError($"[VATBaker] 베이킹 실패: {e}");
            }
        }
    }
}
