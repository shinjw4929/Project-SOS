using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Unity.Mathematics;
using Shared;

namespace VATBaker
{
    public static class VATBakeUtility
    {
        public struct ClipSetting
        {
            public AnimationClip Clip;
            public float Fps;
            public bool Loop;
        }

        public struct BakeResult
        {
            public Texture2D PositionTexture;
            public Mesh StaticMesh;
            public VATClipDataAsset ClipDataAsset;
            public Material[] VATMaterials;
        }

        public static AnimationClip[] ExtractClips(GameObject fbxPrefab)
        {
            string assetPath = AssetDatabase.GetAssetPath(fbxPrefab);
            if (string.IsNullOrEmpty(assetPath))
                return Array.Empty<AnimationClip>();

            var clips = new List<AnimationClip>();
            foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(assetPath))
            {
                if (obj is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                    clips.Add(clip);
            }
            return clips.ToArray();
        }

        public static BakeResult Bake(GameObject fbxPrefab, ClipSetting[] clipSettings, string modelName)
        {
            var result = new BakeResult();
            var instance = GameObject.Instantiate(fbxPrefab);

            try
            {
                var smrList = new List<SkinnedMeshRenderer>(instance.GetComponentsInChildren<SkinnedMeshRenderer>());
                if (smrList.Count == 0)
                    throw new InvalidOperationException("SkinnedMeshRenderer를 찾을 수 없습니다.");

                Debug.Log($"[VATBaker] SkinnedMeshRenderer {smrList.Count}개 발견 — 모두 병합합니다.");

                // 전체 버텍스 수 계산 (모든 SMR 합산)
                int totalVertexCount = 0;
                var smrVertexCounts = new int[smrList.Count];
                var smrVertexOffsets = new int[smrList.Count];
                for (int s = 0; s < smrList.Count; s++)
                {
                    smrVertexOffsets[s] = totalVertexCount;
                    smrVertexCounts[s] = smrList[s].sharedMesh.vertexCount;
                    totalVertexCount += smrVertexCounts[s];
                }

                // 클립별 프레임 수 계산
                int totalFrames = 0;
                var clipFrameCounts = new int[clipSettings.Length];
                for (int i = 0; i < clipSettings.Length; i++)
                {
                    clipFrameCounts[i] = Mathf.Max(1, Mathf.RoundToInt(clipSettings[i].Clip.length * clipSettings[i].Fps));
                    totalFrames += clipFrameCounts[i];
                }

                if (totalVertexCount > 4096)
                    Debug.LogWarning($"[VATBaker] 총 버텍스 수 ({totalVertexCount})가 4096을 초과합니다.");
                if (totalFrames > 4096)
                    Debug.LogWarning($"[VATBaker] 총 프레임 수 ({totalFrames})가 4096을 초과합니다.");

                // Position Texture (RGBAHalf)
                var positionTexture = new Texture2D(totalVertexCount, totalFrames, TextureFormat.RGBAHalf, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
                var rawData = positionTexture.GetRawTextureData<half4>();

                // 바인드포즈용 임시 저장소
                var tempMeshes = new Mesh[smrList.Count];
                for (int s = 0; s < smrList.Count; s++)
                    tempMeshes[s] = new Mesh();

                List<Vector3> bindVertices = null;
                List<Vector3> bindNormals = null;
                List<Vector4> bindTangents = null;
                List<Vector2> bindUV = null;
                List<int[]> bindTrianglesPerSubmesh = null;

                int currentRow = 0;

                AnimationMode.StartAnimationMode();
                try
                {
                    for (int clipIdx = 0; clipIdx < clipSettings.Length; clipIdx++)
                    {
                        var clip = clipSettings[clipIdx].Clip;
                        int frameCount = clipFrameCounts[clipIdx];

                        for (int frame = 0; frame < frameCount; frame++)
                        {
                            float time = (frameCount > 1) ? (frame / (float)(frameCount - 1)) * clip.length : 0f;

                            AnimationMode.BeginSampling();
                            AnimationMode.SampleAnimationClip(instance, clip, time);
                            AnimationMode.EndSampling();

                            // 모든 SMR 베이킹 후 Position Texture에 연속 기록
                            for (int s = 0; s < smrList.Count; s++)
                            {
                                smrList[s].BakeMesh(tempMeshes[s], true);
                                var vertices = tempMeshes[s].vertices;
                                int offset = smrVertexOffsets[s];

                                for (int v = 0; v < smrVertexCounts[s]; v++)
                                {
                                    int pixelIndex = currentRow * totalVertexCount + offset + v;
                                    rawData[pixelIndex] = new half4(
                                        new half(vertices[v].x),
                                        new half(vertices[v].y),
                                        new half(vertices[v].z),
                                        new half(1.0f));
                                }
                            }

                            // 바인드포즈 (첫 프레임)
                            if (bindVertices == null)
                            {
                                bindVertices = new List<Vector3>(totalVertexCount);
                                bindNormals = new List<Vector3>(totalVertexCount);
                                bindTangents = new List<Vector4>(totalVertexCount);
                                bindUV = new List<Vector2>(totalVertexCount);
                                bindTrianglesPerSubmesh = new List<int[]>();

                                for (int s = 0; s < smrList.Count; s++)
                                {
                                    var m = tempMeshes[s];
                                    int vOffset = smrVertexOffsets[s];

                                    bindVertices.AddRange(m.vertices);
                                    bindNormals.AddRange(m.normals);
                                    bindTangents.AddRange(m.tangents);
                                    bindUV.AddRange(m.uv);

                                    // 서브메시 인덱스를 버텍스 오프셋만큼 시프트
                                    int subCount = m.subMeshCount;
                                    for (int sub = 0; sub < subCount; sub++)
                                    {
                                        var tris = m.GetTriangles(sub);
                                        for (int t = 0; t < tris.Length; t++)
                                            tris[t] += vOffset;
                                        bindTrianglesPerSubmesh.Add(tris);
                                    }
                                }
                            }

                            currentRow++;

                            float progress = (float)currentRow / totalFrames;
                            if (EditorUtility.DisplayCancelableProgressBar(
                                "VAT Baking",
                                $"[{clipIdx + 1}/{clipSettings.Length}] {clip.name} - 프레임 {frame + 1}/{frameCount}",
                                progress))
                            {
                                throw new OperationCanceledException("사용자가 베이킹을 취소했습니다.");
                            }
                        }
                    }
                }
                finally
                {
                    AnimationMode.StopAnimationMode();
                    EditorUtility.ClearProgressBar();
                }

                positionTexture.Apply();

                // 병합 바인드포즈 메시 생성
                var bindPoseMesh = new Mesh();
                if (totalVertexCount > 65535)
                    bindPoseMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

                bindPoseMesh.SetVertices(bindVertices);
                bindPoseMesh.SetNormals(bindNormals);
                bindPoseMesh.SetTangents(bindTangents);
                bindPoseMesh.SetUVs(0, bindUV);

                bindPoseMesh.subMeshCount = bindTrianglesPerSubmesh.Count;
                for (int s = 0; s < bindTrianglesPerSubmesh.Count; s++)
                    bindPoseMesh.SetTriangles(bindTrianglesPerSubmesh[s], s);

                // UV2 인코딩 (정규화 금지)
                var uv2 = new Vector2[totalVertexCount];
                for (int i = 0; i < totalVertexCount; i++)
                    uv2[i] = new Vector2(i, 0);
                bindPoseMesh.SetUVs(1, uv2);
                bindPoseMesh.name = $"{modelName}_BindPose";

                // 출력 경로 보장
                string outputPath = $"Assets/VATData/{modelName}";
                EnsureFolderExists(outputPath);

                AssetDatabase.CreateAsset(positionTexture, $"{outputPath}/{modelName}_Positions.asset");
                result.PositionTexture = positionTexture;

                AssetDatabase.CreateAsset(bindPoseMesh, $"{outputPath}/{modelName}_BindPose.asset");
                result.StaticMesh = bindPoseMesh;

                // VATClipDataAsset
                var clipDataAsset = ScriptableObject.CreateInstance<VATClipDataAsset>();
                clipDataAsset.PositionTexture = positionTexture;
                clipDataAsset.StaticMesh = bindPoseMesh;
                clipDataAsset.Clips = new VATClipDataAsset.ClipEntry[clipSettings.Length];

                int startRow = 0;
                for (int i = 0; i < clipSettings.Length; i++)
                {
                    clipDataAsset.Clips[i] = new VATClipDataAsset.ClipEntry
                    {
                        Name = clipSettings[i].Clip.name,
                        StartRow = startRow,
                        RowCount = clipFrameCounts[i],
                        Fps = clipSettings[i].Fps,
                        Loop = clipSettings[i].Loop
                    };
                    startRow += clipFrameCounts[i];
                }

                AssetDatabase.CreateAsset(clipDataAsset, $"{outputPath}/{modelName}_ClipData.asset");
                result.ClipDataAsset = clipDataAsset;

                // VAT Material 생성 (서브메시별, 원본 텍스처 복사)
                var vatShader = Shader.Find("Custom/VATAnimation");
                if (vatShader == null)
                    throw new InvalidOperationException("Custom/VATAnimation 셰이더를 찾을 수 없습니다.");

                var texelSize = new Vector4(
                    1.0f / positionTexture.width,
                    1.0f / positionTexture.height,
                    0, 0);

                // 원본 머티리얼 수집: 모든 SMR + FBX 에셋
                var allOriginalMaterials = CollectOriginalMaterials(fbxPrefab, smrList);

                int matCount = bindPoseMesh.subMeshCount;
                var vatMaterials = new Material[matCount];

                for (int m = 0; m < matCount; m++)
                {
                    var vatMat = new Material(vatShader);
                    vatMat.SetTexture("_VATPositionTex", positionTexture);
                    vatMat.SetVector("_VATTexelSize", texelSize);
                    vatMat.enableInstancing = true;

                    Texture copiedTex = null;
                    Color? copiedColor = null;

                    if (m < allOriginalMaterials.Count)
                        FindTextureAndColor(allOriginalMaterials[m], out copiedTex, out copiedColor);

                    if (copiedTex == null)
                    {
                        foreach (var mat in allOriginalMaterials)
                        {
                            FindTextureAndColor(mat, out copiedTex, out copiedColor);
                            if (copiedTex != null) break;
                        }
                    }

                    if (copiedTex != null)
                    {
                        vatMat.SetTexture("_BaseMap", copiedTex);
                        Debug.Log($"[VATBaker] 머티리얼 {m}: 텍스처 '{copiedTex.name}' 복사 완료");
                    }
                    else
                    {
                        Debug.LogWarning($"[VATBaker] 머티리얼 {m}: 텍스처를 찾지 못했습니다.");
                    }

                    if (copiedColor.HasValue)
                        vatMat.SetColor("_BaseColor", copiedColor.Value);

                    string matFileName = matCount == 1
                        ? $"{modelName}_VAT.mat"
                        : $"{modelName}_VAT_{m}.mat";
                    AssetDatabase.CreateAsset(vatMat, $"{outputPath}/{matFileName}");
                    vatMaterials[m] = vatMat;
                }

                result.VATMaterials = vatMaterials;

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                Debug.Log($"[VATBaker] 베이킹 완료: {outputPath}/ (SMR: {smrList.Count}, 버텍스: {totalVertexCount}, 프레임: {totalFrames}, 클립: {clipSettings.Length}, 서브메시: {matCount})");
                return result;
            }
            finally
            {
                GameObject.DestroyImmediate(instance);
                EditorUtility.ClearProgressBar();
            }
        }

        private static List<Material> CollectOriginalMaterials(GameObject fbxPrefab, List<SkinnedMeshRenderer> instanceSmrList)
        {
            var materials = new List<Material>();

            // 1. 모든 인스턴스 SMR의 sharedMaterials (순서대로)
            foreach (var smr in instanceSmrList)
            {
                if (smr.sharedMaterials == null) continue;
                foreach (var mat in smr.sharedMaterials)
                {
                    if (mat != null)
                        materials.Add(mat); // 중복 허용 (서브메시 인덱스 매칭 유지)
                }
            }

            // 2. 프리팹 원본 SMR들
            var prefabSmrList = fbxPrefab.GetComponentsInChildren<SkinnedMeshRenderer>();
            foreach (var smr in prefabSmrList)
            {
                if (smr.sharedMaterials == null) continue;
                foreach (var mat in smr.sharedMaterials)
                {
                    if (mat != null && !materials.Contains(mat))
                        materials.Add(mat);
                }
            }

            // 3. FBX 에셋 내 Material sub-assets
            string fbxPath = AssetDatabase.GetAssetPath(fbxPrefab);
            if (!string.IsNullOrEmpty(fbxPath))
            {
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
                {
                    if (asset is Material mat && !materials.Contains(mat))
                        materials.Add(mat);
                }
            }

            return materials;
        }

        private static void FindTextureAndColor(Material mat, out Texture texture, out Color? color)
        {
            texture = null;
            color = null;
            if (mat == null) return;

            if (mat.HasProperty("_BaseMap"))
                texture = mat.GetTexture("_BaseMap");
            if (texture == null && mat.HasProperty("_MainTex"))
                texture = mat.GetTexture("_MainTex");

            if (texture == null)
            {
                var matShader = mat.shader;
                int propCount = matShader.GetPropertyCount();
                for (int p = 0; p < propCount; p++)
                {
                    if (matShader.GetPropertyType(p) == UnityEngine.Rendering.ShaderPropertyType.Texture)
                    {
                        texture = mat.GetTexture(matShader.GetPropertyNameId(p));
                        if (texture != null) break;
                    }
                }
            }

            if (mat.HasProperty("_BaseColor"))
                color = mat.GetColor("_BaseColor");
            else if (mat.HasProperty("_Color"))
                color = mat.GetColor("_Color");
        }

        private static void EnsureFolderExists(string path)
        {
            var parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
