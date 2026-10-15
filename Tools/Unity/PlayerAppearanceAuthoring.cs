using System;
using System.Collections.Generic;
using System.Linq;
using Game;
using UnityEditor;
using UnityEngine;

/// <summary>把正式 City 模型的九套造型及手臂绑定到现有玩家和预览；不修改备用包或场景。</summary>
public static class PlayerAppearanceAuthoring
{
    static readonly string[] Names = { "Character_BusinessMan_Shirt", "Character_BusinessMan_Suit", "Character_BusinessWoman", "Character_Female_Coat", "Character_Female_Jacket", "Character_Female_Police", "Character_Male_Hoodie", "Character_Male_Jacket", "Character_Male_Police" };
    const string Folder = "Assets/Game/Art/Player/WholeBodyArms";
    public static string Run()
    {
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Game/Art/Player", "WholeBodyArms");
        var meshes = new Mesh[Names.Length];
        var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Art/Player/Humanoid/PlayerCityCharacters.fbx");
        var renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < Names.Length; i++)
        {
            var source = renderers.Single(r => r.name == Names[i]);
            var mesh = ExtractArms(source);
            var path = $"{Folder}/{Names[i]}_Arms.asset";
            var previous = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (previous == null) AssetDatabase.CreateAsset(mesh, path);
            else { EditorUtility.CopySerialized(mesh, previous); UnityEngine.Object.DestroyImmediate(mesh); mesh = previous; }
            meshes[i] = mesh;
        }
        foreach (var path in new[] { "Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab", "Assets/Game/Art/Player/OutfitPrototype/ClothingCharacterPreview.prefab" })
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var appearance = root.GetComponent<ClothingAppearanceView>();
                var skins = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                var serialized = new SerializedObject(appearance);
                serialized.FindProperty("modularRoot").objectReferenceValue = root.GetComponentsInChildren<Transform>(true).Single(t => t.name == "ClothingParts").gameObject;
                var variants = serialized.FindProperty("wholeBodies"); variants.arraySize = Names.Length;
                for (int i = 0; i < Names.Length; i++)
                {
                    var skin = skins.SingleOrDefault(r => r.name == Names[i]);
                    if (skin == null)
                    {
                        // 旧预览保留了人物节点但移除了 Renderer；补回正式模型网格并复用预览骨架。
                        var node = root.GetComponentsInChildren<Transform>(true).Single(t => t.name == Names[i]);
                        var template = skins.Single(r => r.name == "Base_Torso");
                        var source = renderers.Single(r => r.name == Names[i]);
                        skin = node.gameObject.AddComponent<SkinnedMeshRenderer>();
                        skin.sharedMesh = source.sharedMesh; skin.sharedMaterials = template.sharedMaterials;
                        skin.bones = template.bones; skin.rootBone = template.rootBone;
                        skin.localBounds = source.localBounds;
                    }
                    // 旧玩家 Prefab 曾禁用整套 Renderer；造型显隐统一由节点控制。
                    skin.enabled = true;
                    skin.gameObject.SetActive(false);
                    skin.forceMatrixRecalculationPerRender = true;
                    var part = variants.GetArrayElementAtIndex(i);
                    part.FindPropertyRelative("Root").objectReferenceValue = skin.gameObject;
                    part.FindPropertyRelative("Sleeves").objectReferenceValue = meshes[i];
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        AssetDatabase.SaveAssets();
        return "已绑定九套造型与对应第一人称手臂；玩家与预览默认仍为分部位模式。";
    }

    /// <summary>按骨骼权重保留手臂三角面并压紧顶点；沿用原绑定矩阵及 49 骨顺序。</summary>
    static Mesh ExtractArms(SkinnedMeshRenderer renderer)
    {
        var source = renderer.sharedMesh;
        var weights = source.boneWeights;
        bool ArmBone(int index) => index >= 9 && index <= 38;
        bool ArmVertex(int index)
        {
            var b = weights[index];
            float sum = (ArmBone(b.boneIndex0) ? b.weight0 : 0) + (ArmBone(b.boneIndex1) ? b.weight1 : 0)
                + (ArmBone(b.boneIndex2) ? b.weight2 : 0) + (ArmBone(b.boneIndex3) ? b.weight3 : 0);
            return sum >= .5f;
        }
        var triangles = source.triangles; var kept = new List<int>();
        for (int i = 0; i < triangles.Length; i += 3)
            if (ArmVertex(triangles[i]) && ArmVertex(triangles[i + 1]) && ArmVertex(triangles[i + 2]))
                kept.AddRange(new[] { triangles[i], triangles[i + 1], triangles[i + 2] });
        if (kept.Count == 0) throw new InvalidOperationException(renderer.name + " 没有可提取的手臂");
        var indices = kept.Distinct().ToArray(); var remap = indices.Select((old, next) => (old, next)).ToDictionary(x => x.old, x => x.next);
        var vertices = source.vertices; var normals = source.normals; var uv = source.uv;
        var mesh = new Mesh { name = renderer.name + "_Arms", vertices = indices.Select(i => vertices[i]).ToArray(),
            normals = indices.Select(i => normals[i]).ToArray(), uv = indices.Select(i => uv[i]).ToArray(),
            boneWeights = indices.Select(i => weights[i]).ToArray(), bindposes = source.bindposes,
            triangles = kept.Select(i => remap[i]).ToArray() };
        mesh.RecalculateBounds();
        return mesh;
    }
}
