using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>农场去雪同源制作入口；候选与正式回读均隔离渲染，仅明确采用后调用正式保存入口。</summary>
public static class FarmGroundPreview
{
    // 仅用于临时相机隔离；所有实例和自建素材随预览结束销毁。
    private const int PreviewLayer = 30;
    private const string FarmPath = "Assets/Game/Prefabs/SceneObjects/ArcadeFarmVisual.prefab";
    // 独立保存新网格与配色，保留原雪组和全部旧素材的GUID。
    private const string GroundAssets = "Assets/Game/Art/Arcade/Farm/Environment/GroundV1";
    private static readonly List<Object> Owned = new List<Object>();

    /// <summary>同机位录制原版、候选全图及正式主角同框近景，并验证原有几何完整保留。</summary>
    public static string Run()
    {
        return Render(false);
    }

    /// <summary>只回读正式资源，同机位检查正式画面与用户已采用候选一致。</summary>
    public static string RunApplied()
    {
        return Render(true);
    }

    /// <summary>候选阶段生成临时配色，正式回读阶段直接使用持久化Prefab，不再次修饰。</summary>
    private static string Render(bool applied)
    {
        if (!Application.dataPath.Replace('\\', '/').EndsWith("/FirepowerLoop/Client/Assets") ||
            EditorApplication.isPlaying || EditorApplication.isCompiling)
            throw new InvalidOperationException("仅本工程空闲编辑态可生成农场临时预览");
        var previousScene = SceneManager.GetActiveScene();
        bool previousDirty = previousScene.isDirty;
        int previousSceneCount = SceneManager.sceneCount;
        var previousTarget = RenderTexture.active;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        Camera camera = null;
        string folder = Path.GetFullPath(applied
            ? "../Docs/Plans/Artifacts/FarmGroundApplied-20261023"
            : "../Docs/Plans/Artifacts/FarmGround-Unity-v1-20261023");
        try
        {
            SceneManager.SetActiveScene(scene);
            Directory.CreateDirectory(folder);
            var root = new GameObject("TEMP_FARM_GROUND_PREVIEW").transform;
            var environment = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(FarmPath), root);
            if (environment == null) throw new InvalidOperationException("正式农场Prefab未找到");
            var sourceMeshes = environment.GetComponentsInChildren<MeshFilter>(true)
                .ToDictionary(f => f.name, f => f.sharedMesh);
            AddScaleReferences(root);

            var light = new GameObject("PreviewSun", typeof(Light)).GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.cullingMask = 1 << PreviewLayer;
            light.transform.rotation = Quaternion.Euler(48, -30, 0);
            camera = new GameObject("PreviewCamera", typeof(Camera)).GetComponent<Camera>();
            camera.orthographic = true;
            camera.cullingMask = 1 << PreviewLayer;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.06f, .09f, .11f);
            camera.farClipPlane = 250;
            SetLayers(root);

            MeshFilter snow;
            int[] categoryTriangles;
            const int patchCount = 130;
            if (!applied)
            {
                if (sourceMeshes.Count != 10)
                    throw new InvalidOperationException("正式环境已变更，不能覆盖历史采用候选");
                Overview(camera);
                Capture(camera, folder, "original-overview.png", 1500, 1100);
                Detail(camera);
                Capture(camera, folder, "original-detail.png", 1400, 1000);
                snow = environment.GetComponentsInChildren<MeshFilter>(true)
                    .Single(f => AssetDatabase.GetAssetPath(f.sharedMesh).EndsWith("/FarmMesh0.asset"));
                categoryTriangles = ReplaceSnowPalette(snow);
                AddDirtPatches(root);
            }
            else
            {
                snow = environment.GetComponentsInChildren<MeshFilter>(true).Single(f => f.name == "EnvironmentPart0");
                var originalSnow = AssetDatabase.LoadAssetAtPath<Mesh>(
                    "Assets/Game/Art/Arcade/Farm/Environment/FarmMesh0.asset");
                if (!SameGeometry(snow.sharedMesh, originalSnow))
                    throw new InvalidOperationException("正式去雪网格改变了原有几何");
                categoryTriangles = Enumerable.Range(0, snow.sharedMesh.subMeshCount)
                    .Select(i => (int)snow.sharedMesh.GetIndexCount(i) / 3).ToArray();
                if (environment.GetComponentsInChildren<MeshFilter>(true).Count(f => f.name == "GroundPatches") != 1)
                    throw new InvalidOperationException("正式色斑缺失或重复");
            }
            SetLayers(root);

            // 原场地的全部顶点与三角形保持；仅雪组的三角形按用途分配临时材质。
            foreach (var filter in environment.GetComponentsInChildren<MeshFilter>(true))
            {
                var source = sourceMeshes[filter.name];
                if (!SameGeometry(filter.sharedMesh, source))
                    throw new InvalidOperationException("原有农场几何发生变化：" + filter.name);
                if (filter != snow && filter.sharedMesh != source)
                    throw new InvalidOperationException("非雪组被意外替换：" + filter.name);
            }
            if (environment.GetComponentsInChildren<Collider>(true).Length != 0 ||
                root.GetComponentsInChildren<Collider>(true).Any(c => c.enabled))
                throw new InvalidOperationException("外观预览不允许新增有效阻挡");

            Overview(camera);
            Capture(camera, folder, "candidate-overview.png", 1500, 1100);
            Detail(camera);
            Capture(camera, folder, "candidate-detail.png", 1400, 1000);
            camera.orthographicSize = 4.6f;
            camera.transform.position = new Vector3(-7, 7, -25);
            camera.transform.LookAt(new Vector3(-14.3f, .7f, -16));
            Capture(camera, folder, "candidate-player-scale.png", 1400, 1000);

            string evidence = "project=" + Application.dataPath + "\nsource=" + FarmPath +
                "\nsourceMeshCount=" + sourceMeshes.Count + "\noriginalVerticesAndTrianglesPreserved=true" +
                "\nnonSnowMeshesUnchanged=true\nactiveColliders=0\ndirtPatchCount=" + patchCount +
                "\ntrianglesByGroundHayStoneRoofFoliage=" + string.Join(",", categoryTriangles) +
                "\nusingFormalAssets=" + applied + "\nproductionAssetsSavedDuringRender=false\n";
            File.WriteAllText(Path.Combine(folder, "geometry-verification.txt"), evidence);
            return folder + "\n" + evidence;
        }
        finally
        {
            if (camera != null) camera.targetTexture = null;
            RenderTexture.active = previousTarget;
            SceneManager.SetActiveScene(previousScene);
            EditorSceneManager.CloseScene(scene, true);
            foreach (var asset in Owned)
                if (asset != null && !EditorUtility.IsPersistent(asset)) Object.DestroyImmediate(asset);
            Owned.Clear();
            if (SceneManager.sceneCount != previousSceneCount || previousScene.isDirty != previousDirty)
                throw new InvalidOperationException("预览结束未完整恢复原场景状态");
        }
    }

    /// <summary>用户明确采用Unity候选后，保存同源素材并更新原Prefab；不改原场景、碰撞或玩法。</summary>
    public static string ApplyConfirmed()
    {
        if (!Application.dataPath.Replace('\\', '/').EndsWith("/FirepowerLoop/Client/Assets") ||
            EditorApplication.isPlaying || EditorApplication.isCompiling)
            throw new InvalidOperationException("仅本工程空闲编辑态可正式接入");
        var prefab = PrefabUtility.LoadPrefabContents(FarmPath);
        try
        {
            if (prefab.GetComponentsInChildren<Transform>(true).Any(t => t.name == "GroundPatches"))
                throw new InvalidOperationException("去雪已接入，拒绝重复叠加地面色斑");
            var snow = prefab.GetComponentsInChildren<MeshFilter>(true)
                .Single(f => AssetDatabase.GetAssetPath(f.sharedMesh).EndsWith("/FarmMesh0.asset"));
            var original = snow.sharedMesh;
            ReplaceSnowPalette(snow);
            AddDirtPatches(prefab.transform);
            var patches = prefab.transform.Find("TEMP_FARM_DirtPatches");
            patches.name = "GroundPatches";
            if (!SameGeometry(snow.sharedMesh, original) || prefab.GetComponentsInChildren<Collider>(true).Length != 0)
                throw new InvalidOperationException("正式接入前几何／碰撞检查失败");
            Directory.CreateDirectory(GroundAssets);
            AssetDatabase.Refresh();
            foreach (var filter in new[] { snow, patches.GetComponent<MeshFilter>() })
            {
                var mesh = filter.sharedMesh;
                string name = filter == snow ? "FarmGroundPalette" : "FarmGroundPatches";
                string path = GroundAssets + "/" + name + ".asset";
                var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (existing == null)
                {
                    mesh.name = name;
                    AssetDatabase.CreateAsset(mesh, path);
                }
                else
                {
                    EditorUtility.CopySerialized(mesh, existing);
                    existing.name = name;
                    AssetDatabase.SaveAssetIfDirty(existing);
                    filter.sharedMesh = existing;
                }
                var renderer = filter.GetComponent<MeshRenderer>();
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    var material = materials[i];
                    string materialName = material.name.Replace("TEMP_FARM_", "FarmGround");
                    string materialPath = GroundAssets + "/" + materialName + ".mat";
                    var saved = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                    if (saved == null)
                    {
                        material.name = materialName;
                        AssetDatabase.CreateAsset(material, materialPath);
                        saved = material;
                    }
                    else
                    {
                        EditorUtility.CopySerialized(material, saved);
                        saved.name = materialName;
                        AssetDatabase.SaveAssetIfDirty(saved);
                    }
                    materials[i] = saved;
                }
                renderer.sharedMaterials = materials;
            }
            foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
                if (!EditorUtility.IsPersistent(filter.sharedMesh))
                    throw new InvalidOperationException("正式Prefab仍引用临时网格");
            foreach (var renderer in prefab.GetComponentsInChildren<MeshRenderer>(true))
                if (renderer.sharedMaterials.Any(m => m == null || !EditorUtility.IsPersistent(m)))
                    throw new InvalidOperationException("正式Prefab仍引用临时材质");
            PrefabUtility.SaveAsPrefabAsset(prefab, FarmPath, out bool success);
            if (!success) throw new InvalidOperationException("正式农场Prefab保存失败");
            return "已按采用的Unity候选接入草地／土斑；原网格和meta保留，新增2网格／8材质，不改碰撞或玩法";
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefab);
            foreach (var asset in Owned)
                if (asset != null && !EditorUtility.IsPersistent(asset)) Object.DestroyImmediate(asset);
            Owned.Clear();
        }
    }

    /// <summary>允许子网格重排，严格核对顶点和每个有向三角形，避免仅比较索引数量漏掉拓扑变化。</summary>
    private static bool SameGeometry(Mesh first, Mesh second)
    {
        if (first == null || second == null || !first.vertices.SequenceEqual(second.vertices)) return false;
        return Faces(first).OrderBy(t => t, StringComparer.Ordinal)
            .SequenceEqual(Faces(second).OrderBy(t => t, StringComparer.Ordinal));
    }

    /// <summary>保持每个三角形内部索引顺序，只在外部排序比较。</summary>
    private static IEnumerable<string> Faces(Mesh mesh)
    {
        var triangles = mesh.triangles;
        for (int i = 0; i < triangles.Length; i += 3)
            yield return triangles[i] + "," + triangles[i + 1] + "," + triangles[i + 2];
    }

    /// <summary>限定农场四轮，使用真实场景Controller与正式资源检查加载、色斑归属和一次释放。</summary>
    public static string VerifyFourRounds()
    {
        if (!Application.dataPath.Replace('\\', '/').EndsWith("/FirepowerLoop/Client/Assets") ||
            EditorApplication.isPlaying || EditorApplication.isCompiling)
            throw new InvalidOperationException("仅本工程编辑态核对农场四轮");
        var previous = Game.ResourceLoader.Current;
        var priorScene = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var results = new List<string>();
        const System.Reflection.BindingFlags hidden = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        try
        {
            SceneManager.SetActiveScene(scene);
            var provider = new Game.LubanRuntimeConfigProvider(() => new global::Tables(n =>
                new Luban.ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath, "Game/Config", n + ".bytes")))));
            if (!provider.TryGetArcadeCatalog(out var catalog, out var error)) throw new InvalidOperationException(error);
            var stage = catalog.EarlyStages.Single(p => p.ArenaConfigNumber == 22);
            var layouts = catalog.RoundLayouts.Where(p => p.ArenaConfigNumber == 22).OrderBy(p => p.RoundNumber).ToArray();
            if (layouts.Length != 4) throw new InvalidOperationException("农场必须明确限定为四个布局");
            foreach (var layout in layouts)
            {
                var loader = new RoundLoader();
                Game.ResourceLoader.Use(loader);
                var root = new GameObject("FarmRound" + layout.RoundNumber);
                var view = new Game.ArcadeCombatViewController();
                try
                {
                    typeof(Game.ArcadeCombatViewController).GetField("root", hidden).SetValue(view, root);
                    typeof(Game.ArcadeCombatViewController).GetField("earlyStage", hidden).SetValue(view, stage);
                    view.ConfigureRoundBounds(stage.HalfWidth, stage.HalfHeight, .55f);
                    view.SetRoundLayout(layout);
                    if (loader.Pending.Count != 2) throw new InvalidOperationException("环境／牛模板请求数异常");
                    loader.Pending[0]();
                    loader.Pending[1]();
                    if (!view.IsRoundLayoutReady ||
                        root.GetComponentsInChildren<Transform>(true).Count(t => t.name == "GroundPatches") != 1 ||
                        root.GetComponentsInChildren<Collider>(true).Length != 0)
                        throw new InvalidOperationException("农场第" + layout.RoundNumber + "轮正式资源未正确就绪");
                    view.End();
                    if (root != null && root.transform.childCount != 0 || loader.Released.Count != 2)
                        throw new InvalidOperationException("农场第" + layout.RoundNumber + "轮释放不完整");
                    view.End();
                    if (loader.Released.Count != 2) throw new InvalidOperationException("资源重复释放");
                    results.Add("round=" + layout.RoundNumber + ":ready,oneGroundPatches,noCollider,releaseOnce");
                }
                finally
                {
                    view.End();
                    if (root != null) Object.DestroyImmediate(root);
                }
            }
            string evidence = string.Join("\n", results) + "\n";
            string folder = Path.GetFullPath("../Docs/Plans/Artifacts/FarmGroundApplied-20261023");
            File.WriteAllText(Path.Combine(folder, "four-round-load-release.txt"), evidence);
            return evidence;
        }
        finally
        {
            Game.ResourceLoader.Use(previous);
            SceneManager.SetActiveScene(priorScene);
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    /// <summary>只替代异步磁盘完成边界，实际请求、实例创建和回收仍归正式Controller。</summary>
    private sealed class RoundLoader : Game.IResourceLoader
    {
        public readonly List<Action> Pending = new List<Action>();
        public readonly HashSet<object> Released = new HashSet<object>();
        public T LoadAsset<T>(string path) where T : Object => throw new InvalidOperationException("不接受额外同步加载");
        public Game.ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> complete) where T : Object
        {
            var identity = new object();
            Pending.Add(() => complete(AssetDatabase.LoadAssetAtPath<T>("Assets/Game/" + path + ".prefab")));
            return new Game.ResourceLoadHandle(identity);
        }
        public void Release(Game.ResourceLoadHandle handle)
        {
            if (!Released.Add(handle.RawHandle)) throw new InvalidOperationException("同一资源Handle重复释放");
        }
    }

    /// <summary>只克隆雪组网格，按高度和已有屋顶位置分配草地、草料、石墙、屋顶与树叶材质。</summary>
    private static int[] ReplaceSnowPalette(MeshFilter filter)
    {
        var source = filter.sharedMesh;
        var vertices = source.vertices;
        var groups = Enumerable.Range(0, 5).Select(_ => new List<int>()).ToArray();
        var indices = source.triangles;
        for (int i = 0; i < indices.Length; i += 3)
        {
            Vector3 a = vertices[indices[i]], b = vertices[indices[i + 1]], c = vertices[indices[i + 2]];
            float top = Mathf.Max(a.y, Mathf.Max(b.y, c.y));
            Vector3 center = (a + b + c) / 3;
            int group = top < .15f ? 0 : top < 1.5f ? 1 : top < 2.5f ? 2 :
                top < 3.1f && Mathf.Abs(center.x) > 20 && center.z > 19 ? 3 : 4;
            groups[group].Add(indices[i]);
            groups[group].Add(indices[i + 1]);
            groups[group].Add(indices[i + 2]);
        }
        if (groups.Any(g => g.Count == 0)) throw new InvalidOperationException("雪组分区缺失，拒绝生成错误候选");
        var mesh = Object.Instantiate(source);
        mesh.name = "TEMP_FARM_NoSnowPalette";
        Owned.Add(mesh);
        mesh.subMeshCount = groups.Length;
        for (int i = 0; i < groups.Length; i++) mesh.SetTriangles(groups[i], i, false);
        mesh.RecalculateBounds();
        filter.sharedMesh = mesh;
        filter.GetComponent<MeshRenderer>().sharedMaterials = new[]
        {
            Material("Grass", new Color(.40f, .43f, .20f)),
            Material("Hay", new Color(.65f, .49f, .24f)),
            Material("Stone", new Color(.30f, .34f, .36f)),
            Material("Roof", new Color(.34f, .20f, .11f)),
            Material("Foliage", new Color(.30f, .37f, .31f))
        };
        return groups.Select(g => g.Count / 3).ToArray();
    }

    /// <summary>临时平面土斑只有视觉、不带碰撞；固定种子保证同版预览可复现。</summary>
    private static int AddDirtPatches(Transform root)
    {
        var random = new System.Random(221023);
        var vertices = new List<Vector3>();
        var triangles = Enumerable.Range(0, 3).Select(_ => new List<int>()).ToArray();
        const int count = 130;
        for (int patch = 0; patch < count; patch++)
        {
            float x = (float)random.NextDouble() * 65 - 32.5f;
            float z = (float)random.NextDouble() * 62 - 31;
            float radius = .45f + (float)random.NextDouble() * 1.1f;
            int first = vertices.Count;
            vertices.Add(new Vector3(x, .032f, z));
            for (int edge = 0; edge < 8; edge++)
            {
                float angle = edge * Mathf.PI / 4;
                float reach = radius * (.7f + (float)random.NextDouble() * .5f);
                vertices.Add(new Vector3(x + Mathf.Cos(angle) * reach, .032f,
                    z + Mathf.Sin(angle) * reach * .7f));
            }
            for (int edge = 0; edge < 8; edge++)
            {
                triangles[patch % 3].Add(first);
                triangles[patch % 3].Add(first + (edge + 1) % 8 + 1);
                triangles[patch % 3].Add(first + edge + 1);
            }
        }
        var mesh = new Mesh { name = "TEMP_FARM_FlatDirt" };
        Owned.Add(mesh);
        mesh.SetVertices(vertices);
        mesh.subMeshCount = triangles.Length;
        for (int i = 0; i < triangles.Length; i++) mesh.SetTriangles(triangles[i], i);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        var go = new GameObject("TEMP_FARM_DirtPatches", typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(root, false);
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        go.GetComponent<MeshRenderer>().sharedMaterials = new[]
        {
            Material("Soil", new Color(.40f, .34f, .22f)),
            Material("GrassLight", new Color(.43f, .46f, .23f)),
            Material("GrassShade", new Color(.37f, .40f, .18f))
        };
        return count;
    }

    /// <summary>正式主角、AK和一头正式牛只作尺寸比较，不演示牛队队形、速度或战斗。</summary>
    private static void AddScaleReferences(Transform root)
    {
        var hero = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"), root);
        hero.transform.position = new Vector3(-13, 0, -16);
        hero.transform.rotation = Quaternion.Euler(0, 30, 0);
        var presenter = hero.GetComponent<Game.CharacterWeaponPresenter>();
        presenter.SetWeaponId("arcade.weapon.basic");
        presenter.SetManualPoseSampling(true);
        presenter.BodyAnimator.Rebind();
        presenter.BodyAnimator.Update(0);
        presenter.Current.Animations.Idle.Clip.SampleAnimation(presenter.BodyAnimator.gameObject, .1f);
        presenter.ApplyOffhandGrip(presenter.Current.Animations.Idle);
        var cow = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Game/Prefabs/SceneObjects/ArcadeHerdVisual.prefab"), root);
        cow.GetComponent<Game.ArcadeHerdVisual>().Bind(22, 1);
        cow.transform.position = new Vector3(-16, 0, -16);
        cow.transform.rotation = Quaternion.Euler(0, 90, 0);
        foreach (var canvas in root.GetComponentsInChildren<Canvas>(true)) canvas.enabled = false;
        foreach (var collider in root.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
    }

    /// <summary>创建并登记临时材质，统一交给finally销毁。</summary>
    private static Material Material(string name, Color color)
    {
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit"))
            { name = "TEMP_FARM_" + name, color = color };
        material.SetFloat("_Smoothness", .1f);
        Owned.Add(material);
        return material;
    }

    /// <summary>隔离到预览层，避免Boot中的相机、灯光和对象进入画面。</summary>
    private static void SetLayers(Transform root)
    {
        foreach (var item in root.GetComponentsInChildren<Transform>(true)) item.gameObject.layer = PreviewLayer;
    }

    /// <summary>沿用正式农场历史预览的全图机位，方便直接比较。</summary>
    private static void Overview(Camera camera)
    {
        camera.orthographicSize = 36;
        camera.transform.position = new Vector3(0, 68, -44);
        camera.transform.LookAt(Vector3.zero);
    }

    /// <summary>近景同时查看主角、草垛、石墙、树木与地面。</summary>
    private static void Detail(Camera camera)
    {
        camera.orthographicSize = 14;
        camera.transform.position = new Vector3(-8, 22, -40);
        camera.transform.LookAt(new Vector3(-15, .5f, -15));
    }

    /// <summary>直接保存Unity实际渲染，释放目标和读回纹理，不经过AI修饰。</summary>
    private static void Capture(Camera camera, string folder, string name, int width, int height)
    {
        var target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            texture.Apply();
            File.WriteAllBytes(Path.Combine(folder, name), texture.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = null;
            RenderTexture.active = previous;
            Object.DestroyImmediate(texture);
            RenderTexture.ReleaseTemporary(target);
        }
    }
}
