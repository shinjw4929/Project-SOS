using UnityEngine;
using UnityEditor;

public static class SkinnedMeshToStaticConverter
{
    [MenuItem("Tools/Convert SkinnedMesh to Static Mesh")]
    static void ConvertSelected()
    {
        var selected = Selection.activeGameObject;
        if (selected == null)
        {
            EditorUtility.DisplayDialog("Error", "GameObject를 선택해주세요.", "OK");
            return;
        }

        var skinnedRenderers = selected.GetComponentsInChildren<SkinnedMeshRenderer>();
        if (skinnedRenderers.Length == 0)
        {
            EditorUtility.DisplayDialog("Error", "SkinnedMeshRenderer가 없습니다.", "OK");
            return;
        }

        // 저장 폴더 생성
        string folderPath = "Assets/BakedMeshes";
        if (!AssetDatabase.IsValidFolder(folderPath))
        {
            AssetDatabase.CreateFolder("Assets", "BakedMeshes");
        }

        int converted = 0;
        foreach (var smr in skinnedRenderers)
        {
            Mesh bakedMesh = new Mesh();
            smr.BakeMesh(bakedMesh);
            bakedMesh.name = smr.name + "_static";

            string meshPath = $"{folderPath}/{selected.name}_{smr.name}_static.asset";
            meshPath = AssetDatabase.GenerateUniqueAssetPath(meshPath);
            AssetDatabase.CreateAsset(bakedMesh, meshPath);

            var go = smr.gameObject;

            var mf = go.GetComponent<MeshFilter>();
            if (mf == null) mf = go.AddComponent<MeshFilter>();

            var mr = go.GetComponent<MeshRenderer>();
            if (mr == null) mr = go.AddComponent<MeshRenderer>();

            mf.sharedMesh = bakedMesh;
            mr.sharedMaterials = smr.sharedMaterials;

            Object.DestroyImmediate(smr);
            converted++;
        }

        var animator = selected.GetComponent<Animator>();
        if (animator != null)
            Object.DestroyImmediate(animator);

        AssetDatabase.SaveAssets();
        EditorUtility.DisplayDialog("Complete", $"{converted}개 변환 완료", "OK");
    }

    [MenuItem("Tools/Convert SkinnedMesh to Static Mesh", true)]
    static bool ValidateConvert()
    {
        return Selection.activeGameObject != null;
    }
}