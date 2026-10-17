using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>以既有光晕和爆炸资源制作独立能量弹及地图爆炸模板，不改碰撞或战斗参数。</summary>
public static class ArcadeSpecialWeaponAuthoring
{
    const string Folder = "Assets/Game/Art/Arcade/VFX/Gunfire/";
    public static string Run()
    {
        if (EditorApplication.isPlaying || Application.dataPath.Replace('\\', '/') != "E:/GitHub/FirepowerLoop/Client/Assets")
            throw new InvalidOperationException("仅本工程编辑态制作");
        var halo = Material("RaygunHalo", new Color(.15f, 1.5f, 3f, .65f));
        var core = Material("RaygunCore", new Color(.8f, 3.5f, 5f, 1));
        const string path = "Assets/Game/Prefabs/Combat/GrayboxArrow.prefab";
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var mesh = BoltMesh(root.transform.localScale);
            string meshPath = Folder + "RaygunBolt.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (existing == null) { AssetDatabase.CreateAsset(mesh, meshPath); existing = mesh; }
            else { EditorUtility.CopySerialized(mesh, existing); Object.DestroyImmediate(mesh); }
            var child = root.transform.Find("ArcadeRaygunBolt");
            if (child == null) { child = new GameObject("ArcadeRaygunBolt", typeof(MeshFilter), typeof(MeshRenderer)).transform; child.SetParent(root.transform, false); }
            child.GetComponent<MeshFilter>().sharedMesh = existing;
            var renderer = child.GetComponent<MeshRenderer>(); renderer.sharedMaterials = new[] { halo, core };
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
            child.gameObject.SetActive(false);
            var serialized = new SerializedObject(root.GetComponent<Game.ProjectileView>());
            var bindings = serialized.FindProperty("arcadeVisuals"); int index = -1;
            for (int i = 0; i < bindings.arraySize; i++)
                if (bindings.GetArrayElementAtIndex(i).FindPropertyRelative("resourceName").stringValue == child.name) index = i;
            if (index < 0) { index = bindings.arraySize; bindings.InsertArrayElementAtIndex(index); }
            var binding = bindings.GetArrayElementAtIndex(index);
            binding.FindPropertyRelative("resourceName").stringValue = child.name;
            binding.FindPropertyRelative("visualRoot").objectReferenceValue = child.gameObject;
            serialized.ApplyModifiedPropertiesWithoutUndo(); PrefabUtility.SaveAsPrefabAsset(root, path);
            // 保留旧 FBX 与绑定根，只禁用不透明方块渲染，补独立的细亮穿透束。
            var piercing = root.transform.Find("ArcadePiercingRay");
            foreach (var skin in piercing.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            { skin.enabled = false; PrefabUtility.RecordPrefabInstancePropertyModifications(skin); }
            var beam = piercing.Find("PiercingStreakVisual");
            if (beam == null) { beam = new GameObject("PiercingStreakVisual", typeof(MeshFilter), typeof(MeshRenderer)).transform; beam.SetParent(piercing, false); }
            var beamMesh = BoltMesh(piercing.lossyScale, true);
            string beamPath = Folder + "PiercingStreak.asset";
            var savedBeam = AssetDatabase.LoadAssetAtPath<Mesh>(beamPath);
            if (savedBeam == null) { AssetDatabase.CreateAsset(beamMesh, beamPath); savedBeam = beamMesh; }
            else { EditorUtility.CopySerialized(beamMesh, savedBeam); Object.DestroyImmediate(beamMesh); }
            beam.GetComponent<MeshFilter>().sharedMesh = savedBeam;
            var beamRenderer = beam.GetComponent<MeshRenderer>();
            beamRenderer.sharedMaterials = new[] { Material("PiercingHalo", new Color(.6f, .3f, 3f, .7f)), Material("PiercingCore", new Color(1.8f, 2.7f, 4.5f, 1)) };
            beamRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; beamRenderer.receiveShadows = false;
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        const string kit = "Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab";
        root = PrefabUtility.LoadPrefabContents(kit);
        try
        {
            var content = root.transform.Find("Content"); var blast = content.Find("ProjectileBlast");
            if (blast != null && !PrefabUtility.IsPartOfPrefabInstance(blast.gameObject)) { Object.DestroyImmediate(blast.gameObject); blast = null; }
            if (blast == null)
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Art/Arcade/VFX/FirstBatch/Prefabs/FireImpact.prefab");
                blast = ((GameObject)PrefabUtility.InstantiatePrefab(source, content)).transform; blast.name = "ProjectileBlast";
            }
            foreach (var ps in blast.GetComponentsInChildren<ParticleSystem>(true))
            {
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main; main.playOnAwake = false; main.loop = false;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                PrefabUtility.RecordPrefabInstancePropertyModifications(ps);
            }
            blast.gameObject.SetActive(false); PrefabUtility.RecordPrefabInstancePropertyModifications(blast.gameObject);
            PrefabUtility.SaveAsPrefabAsset(root, kit);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets(); return "能量弹与既有爆炸副本已保存，原模板和碰撞载体保持";
    }

    /// <summary>两组交叉光晕板使用既有亮芯纹理；按载体缩放反算网格，保持约半米短能量弹。</summary>
    static Mesh BoltMesh(Vector3 scale, bool piercing = false)
    {
        var vertices = new Vector3[16]; var uv = new Vector2[16];
        var mesh = new Mesh { name = "RaygunEnergyBolt", subMeshCount = 2 };
        for (int layer = 0; layer < 2; layer++)
        for (int plane = 0; plane < 2; plane++)
        {
            float width = piercing ? (layer == 0 ? .035f : .016f) : (layer == 0 ? .10f : .045f);
            float length = piercing ? (layer == 0 ? .7f : .6f) : (layer == 0 ? .28f : .20f);
            int start = layer * 8 + plane * 4;
            var corners = new[] { new Vector2(-width, -length), new Vector2(width, -length), new Vector2(width, length), new Vector2(-width, length) };
            for (int i = 0; i < 4; i++)
            {
                var v = plane == 0 ? new Vector3(corners[i].x, 0, corners[i].y) : new Vector3(0, corners[i].x, corners[i].y);
                vertices[start + i] = new Vector3(v.x / scale.x, v.y / scale.y, v.z / scale.z);
                uv[start + i] = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up }[i];
            }
        }
        mesh.vertices = vertices; mesh.uv = uv;
        mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7 }, 0);
        mesh.SetTriangles(new[] { 8, 9, 10, 8, 10, 11, 12, 13, 14, 12, 14, 15 }, 1);
        mesh.RecalculateBounds(); mesh.RecalculateNormals(); return mesh;
    }

    /// <summary>独立材质复用正式亮芯贴图，不改变普通三枪的颜色。</summary>
    static Material Material(string name, Color color)
    {
        string path = Folder + name + ".mat";
        if (!File.Exists(path) && !AssetDatabase.CopyAsset(Folder + "BulletCore.mat", path)) throw new InvalidOperationException(path);
        var material = AssetDatabase.LoadAssetAtPath<Material>(path); material.SetColor("_BaseColor", color);
        EditorUtility.SetDirty(material); return material;
    }
}
