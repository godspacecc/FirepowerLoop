using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>从已筛选贴图制作普通枪弹、枪口和命中资源；不修改弹道规则或第三方原包。</summary>
public static class ArcadeGunfireAuthoring
{
    private const string Folder = "Assets/Game/Art/Arcade/VFX/Gunfire";
    private const string Kit = "Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab";

    /// <summary>已有资源原位更新，保留 GUID；只编辑正式资源副本和当前枪弹表现。</summary>
    public static string Run()
    {
        if (!Application.dataPath.Replace('\\', '/').EndsWith("/FirepowerLoop/Client/Assets"))
            throw new InvalidOperationException("只允许在 FirepowerLoop 中制作");
        if (EditorApplication.isPlaying || EditorApplication.isCompiling) throw new InvalidOperationException("需非播放状态");
        AssetDatabase.Refresh();
        foreach (string name in new[] { "BulletBeamOrange", "GunMuzzleFront", "GunImpactSpark" })
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(Folder + "/Textures/" + name + ".png");
            importer.textureType = TextureImporterType.Default;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.alphaIsTransparency = true;
            importer.maxTextureSize = 1024;
            importer.SaveAndReimport();
        }
        var streak = Material("BulletStreak", "BulletBeamOrange", new Color(4f, 3f, 1.3f, 1));
        var core = Material("BulletCore", "Assets/Game/Art/Effects/ArcadeARPG/Textures/basicglow.png", new Color(4f, 3f, 1.5f, 1));
        var flash = Material("MuzzleFlash", "GunMuzzleFront", new Color(3f, 1.6f, .45f, 1));
        var spark = Material("ImpactSpark", "GunImpactSpark", new Color(3f, 1.25f, .2f, 1));
        string meshPath = Folder + "/BulletStreak.asset";
        var mesh = StreakMesh();
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        if (existing == null) { AssetDatabase.CreateAsset(mesh, meshPath); existing = mesh; }
        else { EditorUtility.CopySerialized(mesh, existing); Object.DestroyImmediate(mesh); }

        EditPrefab("Assets/Game/Prefabs/Combat/GrayboxBullet.prefab", root =>
        {
            root.GetComponent<MeshFilter>().sharedMesh = existing;
            root.GetComponent<MeshRenderer>().sharedMaterials = new[] { streak, core };
        });
        EditPrefab("Assets/Game/Art/Player/FX/PlayerMuzzleFlash.prefab", root =>
        {
            var particles = root.GetComponentsInChildren<ParticleSystem>(true);
            foreach (var ps in particles)
            {
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main;
                main.loop = false; main.playOnAwake = false;
                main.duration = .07f; main.startLifetime = .045f; main.startSpeed = 0f;
                main.startSize = ps.transform == root.transform ? .42f : .25f;
                main.startColor = Color.white; main.maxParticles = 2;
                main.scalingMode = ParticleSystemScalingMode.Shape;
                var emission = ps.emission;
                emission.rateOverTime = 0f;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
                var renderer = ps.GetComponent<ParticleSystemRenderer>();
                renderer.sharedMaterial = flash;
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.alignment = ParticleSystemRenderSpace.View;
            }
        });
        EditPrefab(Kit, root =>
        {
            var content = root.transform.Find("Content");
            if (content == null) throw new InvalidOperationException("正式 Content 模板缺失");
            var previous = content.Find("BulletImpact");
            if (previous != null) Object.DestroyImmediate(previous.gameObject);
            var effect = new GameObject("BulletImpact");
            effect.transform.SetParent(content, false);
            var ps = effect.AddComponent<ParticleSystem>();
            ConfigureImpact(ps, spark);
            effect.SetActive(false);
        });
        AssetDatabase.SaveAssets();
        return "普通枪弹亮芯/短尾、WarFX 枪口与命中模板已接入；未改规则和 Collider";
    }

    /// <summary>选用 URP 粒子无光照加法材质，使远镜头可读性不依赖场地灯光或 Bloom。</summary>
    private static Material Material(string name, string texture, Color color)
    {
        string path = Folder + "/" + name + ".mat";
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) throw new InvalidOperationException("URP 粒子 Shader 缺失");
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
        material.shader = shader;
        string texturePath = texture.StartsWith("Assets/") ? texture : Folder + "/Textures/" + texture + ".png";
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Surface", 1); material.SetFloat("_Blend", 2);
        material.SetFloat("_SrcBlend", (int)BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (int)BlendMode.One);
        material.SetFloat("_ZWrite", 0); material.SetFloat("_Cull", (int)CullMode.Off);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.SetOverrideTag("RenderType", "Transparent");
        material.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>短尾随弹体朝向移动，不存历史轨迹；对象池瞬移时天然不会拉出跨场地长线。</summary>
    private static Mesh StreakMesh()
    {
        var vertices = new List<Vector3>(); var uv = new List<Vector2>();
        var triangles = new List<int>(); var colors = new List<Color>();
        foreach (bool vertical in new[] { false, true })
        {
            int start = vertices.Count;
            Vector3[] world = vertical
                ? new[] { new Vector3(0, -.025f, -.8f), new Vector3(0, .025f, -.8f), new Vector3(0, .05f, .1f), new Vector3(0, -.05f, .1f) }
                : new[] { new Vector3(-.025f, 0, -.8f), new Vector3(.025f, 0, -.8f), new Vector3(.12f, 0, .1f), new Vector3(-.12f, 0, .1f) };
            foreach (var point in world) vertices.Add(new Vector3(point.x / .08f, point.y / .08f, point.z / .22f));
            uv.AddRange(new[] { new Vector2(.3f, 0), new Vector2(.7f, 0), new Vector2(.7f, 1), new Vector2(.3f, 1) });
            colors.AddRange(new[] { new Color(1, .35f, .06f, 0), new Color(1, .35f, .06f, 0), Color.white, Color.white });
            triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
        }
        var coreTriangles = new List<int>();
        foreach (bool vertical in new[] { false, true })
        {
            int start = vertices.Count;
            Vector3[] world = vertical
                ? new[] { new Vector3(0, -.05f, -.12f), new Vector3(0, .05f, -.12f), new Vector3(0, .05f, .1f), new Vector3(0, -.05f, .1f) }
                : new[] { new Vector3(-.1f, .002f, -.12f), new Vector3(.1f, .002f, -.12f), new Vector3(.1f, .002f, .1f), new Vector3(-.1f, .002f, .1f) };
            foreach (var point in world) vertices.Add(new Vector3(point.x / .08f, point.y / .08f, point.z / .22f));
            uv.AddRange(new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up });
            colors.AddRange(new[] { Color.white, Color.white, Color.white, Color.white });
            coreTriangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
        }
        var mesh = new Mesh { name = "OrdinaryBulletShortStreak", subMeshCount = 2 };
        mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetColors(colors); mesh.SetTriangles(triangles, 0); mesh.SetTriangles(coreTriangles, 1);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>命中只播短火星，无碰撞、音频或第三方生命周期脚本。</summary>
    private static void ConfigureImpact(ParticleSystem ps, Material material)
    {
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.duration = .2f; main.loop = false; main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(.09f, .18f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(.05f, .09f);
        main.startColor = Color.white; main.maxParticles = 8;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = ps.emission; emission.rateOverTime = 0;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0, 6) });
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = .035f;
        var fade = ps.colorOverLifetime; fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(new Color(1, .3f, .02f), 1) },
            new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) });
        fade.color = gradient;
        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.lengthScale = 2f; renderer.velocityScale = .03f;
    }

    /// <summary>通过 Unity 正式反序列化和保存局部修改，保留其余 Prefab 数据与引用。</summary>
    private static void EditPrefab(string path, Action<GameObject> edit)
    {
        var root = PrefabUtility.LoadPrefabContents(path);
        try { edit(root); PrefabUtility.SaveAsPrefabAsset(root, path); }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
}
