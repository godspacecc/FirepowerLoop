using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using FirepowerLoop.Arcade;
using Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>牛群声音／薄尘候选的隔离录制；只克隆正式模型并输出临时帧，不保存任何正式素材。</summary>
public static class HerdDustFeedbackPreview
{
    private const float Speed = 7.15f, Hit = 9.1f, Lane = -14f;
    private const int Width = 1280, Height = 720, Layer = 30, Frames = 232;
    private sealed class Stamp
    {
        public float Time, X, Z; public int Cow, Kind, Pair;
    }
    private static readonly List<Object> Owned = new List<Object>();
    /// <summary>先输出代表帧检查透明度、正式角色体量及机位，不冒充完整动态验证。</summary>
    public static string Probe() { return Render(true); }
    /// <summary>录制11.6秒、20fps临时动态，音效另依同源落蹄时间表合成。</summary>
    public static string Run() { return Render(false); }
    /// <summary>正式接入后的隔离回读直接使用生产反馈类；声音事件记录后以同源片段离线混音。</summary>
    public static string RunApplied() { return Render(false, true); }

    private static string Render(bool probe, bool applied=false)
    {
        if (Application.dataPath.Replace('\\', '/') != "E:/GitHub/FirepowerLoop/Client/Assets" ||
            EditorApplication.isPlaying || EditorApplication.isCompiling)
            throw new InvalidOperationException("仅本项目空闲编辑态允许制作隔离候选");
        var prior = SceneManager.GetActiveScene(); var dirty = prior.isDirty; int sceneCount = SceneManager.sceneCount;
        var priorRT = RenderTexture.active;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        Camera camera = null; RenderTexture rt = null; Texture2D pixels = null; ArcadeHerdFeedback formal=null;
        string folder = Path.GetFullPath(applied?"Temp/HerdDustFeedbackApplied-v1-20261023":"Temp/HerdDustFeedback-v1-20261023");
        Directory.CreateDirectory(folder + "/frames");
        try
        {
            SceneManager.SetActiveScene(scene); Owned.Clear();
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.62f, .66f, .7f);
            var root = new GameObject("TEMP_HERD_DUST_FEEDBACK").transform;
            var farm = Clone("Assets/Game/Prefabs/SceneObjects/ArcadeFarmVisual.prefab", root);
            var cows = new ArcadeHerdVisual[5];
            for (int i = 0; i < 5; i++)
            {
                cows[i] = Clone("Assets/Game/Prefabs/SceneObjects/ArcadeHerdVisual.prefab", root).GetComponent<ArcadeHerdVisual>();
                cows[i].Bind(22, i + 1);
            }
            var hero = Clone("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab", root);
            hero.transform.position = new Vector3(-13, 0, -17);
            hero.transform.rotation = Quaternion.Euler(0, 45, 0);
            var presenter = hero.GetComponent<CharacterWeaponPresenter>();
            presenter.SetWeaponId("arcade.weapon.basic"); presenter.SetManualPoseSampling(true);
            presenter.BodyAnimator.Rebind(); presenter.BodyAnimator.Update(0);
            presenter.Current.Animations.Idle.Clip.SampleAnimation(presenter.BodyAnimator.gameObject, .1f);
            presenter.ApplyOffhandGrip(presenter.Current.Animations.Idle);
            var zombie = Clone("Assets/Game/Prefabs/Characters/ArcadeZombieShambler.prefab", root);
            var enemy = zombie.GetComponent<EnemyView>(); enemy.BeginPresentationUse(); enemy.SetNavigationEnabled(false);
            enemy.Animator.Rebind(); enemy.Animator.Update(0);
            // 只在隔离场景显示现有腾空轨迹以观察尘团，不模拟新伤害或玩家落点方案。
            zombie.transform.rotation = Quaternion.Euler(0, -90, 0);
            BakeSkins(hero); BakeSkins(zombie);
            foreach (var canvas in root.GetComponentsInChildren<Canvas>(true)) canvas.enabled = false;
            foreach (var collider in root.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            var sun = new GameObject("PreviewSun", typeof(Light)).GetComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.15f; sun.cullingMask = 1 << Layer;
            sun.transform.rotation = Quaternion.Euler(48, -30, 0);
            camera = new GameObject("PreviewCamera", typeof(Camera)).GetComponent<Camera>();
            camera.orthographic = true; camera.cullingMask = 1 << Layer; camera.farClipPlane = 250;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.06f, .09f, .11f);
            var dust = applied?null:MakeDust(root); var stamps = MakeStamps();
            float now=0;var actualEvents=new List<string>{"time,x,z,cow,kind,pair"};
            if(applied)formal=new ArcadeHerdFeedback(root,cows[0].DustMaterial,(name,position,scale)=>
            {
                int kind=name.Contains("Hoof")?0:name.EndsWith("Scrape")?1:name.EndsWith("Impact")?2:3;
                int tone=name.EndsWith("Hoof2")?1:name.EndsWith("Hoof3")?2:name.EndsWith("Hoof4")?3:0;
                actualEvents.Add(string.Join(",",now.ToString("R",CultureInfo.InvariantCulture),position.x.ToString("R",CultureInfo.InvariantCulture),
                    position.y.ToString("R",CultureInfo.InvariantCulture),0,kind,tone));
            },()=>{});
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
            rt = new RenderTexture(Width, Height, 24); pixels = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            camera.targetTexture = rt;
            int maximum = 0;
            var samples = probe ? new[] { 0, 27, 65, 108, 138, 155, 181, 186, 200 } : Enumerable.Range(0, Frames).ToArray();
            foreach (int frame in samples)
            {
                float t = frame / 20f;
                now=t;var poses=new List<ArcadeHerdSnapshot>();
                if(applied&&(frame==128||frame==168)){formal.SetEnabled(false);formal.SetEnabled(true);}
                hero.SetActive(t < 6.4f); zombie.SetActive(t >= 8.4f);
                for (int i = 0; i < cows.Length; i++)
                {
                    cows[i].gameObject.SetActive(t < 6.4f || i == 0);
                    if (!cows[i].gameObject.activeSelf) continue;
                    float phase; var p = Position(t, i, out phase);
                    if(applied)phase=t<.8f?t:phase-(t<6.4f?i*.07f:0);
                    // 每头保持现有尺寸与3米纵队，只给候选落蹄加入轻微交错相位。
                    var pose=new ArcadeHerdSnapshot(22, i + 1, t < .8f ? ArcadeHerdPhase.Windup : ArcadeHerdPhase.Charging,
                        p.x, p.z, 1, 0, phase);cows[i].Project(pose);poses.Add(pose);
                }
                if (t < 6.4f)
                {
                    float phase; var lead = Position(t, 0, out phase);
                    var focus = new Vector3(lead.x - 6, .5f, Lane);
                    camera.orthographicSize = 10.7f; camera.transform.position = focus + new Vector3(2, 21, -29);
                    camera.transform.LookAt(focus);
                }
                else if (t < 8.4f)
                {
                    float phase; var lead = Position(t, 0, out phase); var focus = lead + new Vector3(-.2f, .45f, 0);
                    camera.orthographicSize = 2.4f; camera.transform.position = focus + new Vector3(2, 3.7f, -7);
                    camera.transform.LookAt(focus);
                }
                else
                {
                    var focus = new Vector3(-4, .8f, Lane);
                    camera.orthographicSize = 4.8f; camera.transform.position = focus + new Vector3(2, 8, -12);
                    camera.transform.LookAt(focus);
                    float flight = Mathf.Clamp01((t - Hit) / .95f);
                    zombie.transform.position = new Vector3(-3.7f + 3 * flight, 2.5f * 4 * flight * (1 - flight), Lane);
                    zombie.transform.rotation = Quaternion.AngleAxis(-85 * Mathf.Clamp01(flight * 3), Vector3.forward) * Quaternion.Euler(0, -90, 0);
                }
                var particles = Particles(t, stamps);
                maximum = Mathf.Max(maximum, particles.Length);
                if(applied){formal.Sync(poses);if(frame==182)formal.Impact(new Vector2(-3.7f,Lane));formal.Tick(t>=6.4f&&t<8.4f?.0125f:.05f,camera);}
                else ProjectDust(dust, camera, particles);
                camera.Render(); RenderTexture.active = rt;
                pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); pixels.Apply();
                File.WriteAllBytes(Path.Combine(folder, probe ? $"probe-{frame:D3}.png" : $"frames/{frame:D3}.png"), pixels.EncodeToPNG());
            }
            File.WriteAllLines(folder + "/events.csv", applied?actualEvents: new[] { "time,x,z,cow,kind,pair" }.Concat(stamps.Select(e => string.Join(",",
                e.Time.ToString("R", CultureInfo.InvariantCulture), e.X.ToString("R", CultureInfo.InvariantCulture), e.Z.ToString("R", CultureInfo.InvariantCulture), e.Cow, e.Kind, e.Pair))));
            string report = "project=" + Application.dataPath + "\npreviewOnly=true\nproductionAssetsWritten=false\nframes=" + samples.Length +
                "\nfps=20\nmainSpeed=7.15\nspacing=3\n"+(applied?"candidateStampEstimate=":"maxDustParticles=") + maximum + "\nfootCloseupQuarterSpeed=true\nimpactTime=9.1\nusingProductionFeedbackClass="+applied+"\n";
            File.WriteAllText(folder + (probe ? "/probe.txt" : "/capture.txt"), report);
            return folder + "\n" + report;
        }
        finally
        {
            formal?.Dispose();
            if (camera != null) camera.targetTexture = null;
            RenderTexture.active = priorRT; SceneManager.SetActiveScene(prior); EditorSceneManager.CloseScene(scene, true);
            if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
            if (pixels != null) Object.DestroyImmediate(pixels);
            foreach (var asset in Owned) if (asset != null && !EditorUtility.IsPersistent(asset)) Object.DestroyImmediate(asset);
            Owned.Clear();
            if (SceneManager.sceneCount != sceneCount || prior.isDirty != dirty) throw new InvalidOperationException("隔离预览未完整恢复场景状态");
        }
    }

    private static GameObject Clone(string path, Transform parent)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (source == null) throw new InvalidOperationException("缺少正式参考：" + path);
        return Object.Instantiate(source, parent);
    }

    /// <summary>固定相位与世界位置供音画共用；近景明确采用四分之一速度。</summary>
    private static Vector3 Position(float time, int cow, out float phase)
    {
        if (time < 6.4f)
        {
            float active = Mathf.Max(0, time - .8f); phase = active + cow * .07f;
            return new Vector3(-18 + Speed * active - cow * 3, 0, Lane);
        }
        if (time < 8.4f) { phase = (time - 6.4f) * .25f + .4f; return new Vector3(-10 + Speed * (time - 6.4f) * .25f, 0, Lane); }
        phase = time - 8.4f; return new Vector3(-10 + Speed * phase, 0, Lane);
    }

    /// <summary>每次对角落蹄记录一次事件；渲染与后期声音严格使用相同时间表。</summary>
    private static List<Stamp> MakeStamps()
    {
        var events = new List<Stamp>();
        for (int segment = 0; segment < 3; segment++)
        {
            float begin = segment == 0 ? .8f : segment == 1 ? 6.4f : 8.4f;
            float end = segment == 0 ? 6.4f : segment == 1 ? 8.4f : 11.6f;
            float rate = segment == 1 ? .25f : 1;
            for (int cow = 0; cow < (segment == 0 ? 5 : 1); cow++)
            {
                float phase0 = segment == 0 ? cow * .07f : segment == 1 ? .4f : 0;
                for (int step = 0; step < 100; step++)
                {
                    float phase = (Mathf.PI * .5f + step * Mathf.PI) / 15;
                    float time = begin + (phase - phase0) / rate;
                    if (time < begin || time >= end) continue;
                    float ignore; var p = Position(time, cow, out ignore);
                    events.Add(new Stamp { Time = time, X = p.x, Z = p.z, Cow = cow, Pair = step % 2 });
                }
            }
        }
        foreach (float t in new[] { .25f, .6f }) events.Add(new Stamp { Time = t, X = -17.3f, Z = Lane, Cow = 0, Kind = 1 });
        events.Add(new Stamp { Time = Hit, X = -3.7f, Z = Lane, Cow = 0, Kind = 2 });
        return events.OrderBy(e => e.Time).ToList();
    }

    /// <summary>有限寿命的贴地尘团，切镜时剔除上一段事件，避免旧轨迹突然出现。</summary>
    private static ParticleSystem.Particle[] Particles(float time, List<Stamp> stamps)
    {
        var particles = new List<ParticleSystem.Particle>();
        float segmentStart = time < 6.4f ? 0 : time < 8.4f ? 6.4f : 8.4f;
        float slow = time >= 6.4f && time < 8.4f ? .25f : 1;
        foreach (var e in stamps)
        {
            float age = (time - e.Time) * slow; float life = e.Kind == 2 ? .52f : .65f;
            if (e.Time < segmentStart || e.Time > time || age >= life) continue;
            var random = new System.Random((int)(e.Time * 10000) + e.Cow * 31);
            int count = e.Kind == 2 ? 14 : e.Kind == 1 ? 4 : 8;
            for (int i = 0; i < count; i++)
            {
                float x = (float)random.NextDouble() - .5f, z = (float)random.NextDouble() - .5f;
                float u = age / life; bool front = i < 4;
                float footX = e.Kind == 0 ? (front ? .63f : -.63f) : 0;
                float footZ = e.Kind == 0 ? (front == (e.Pair == 0) ? .35f : -.35f) : 0;
                var p = new ParticleSystem.Particle();
                p.position = new Vector3(e.X + footX + x * (.35f + age) - age * .4f,
                    .09f + age * .27f, e.Z + footZ + z * (.25f + age));
                p.startLifetime = p.remainingLifetime = 1;
                p.startSize = (e.Kind == 2 ? .27f : .21f) + u * (e.Kind == 2 ? .65f : .55f);
                p.startColor = new Color(.55f, .39f, .18f, (e.Kind == 2 ? .48f : .45f) * Mathf.Pow(1 - u, 1.2f));
                p.rotation = (float)random.NextDouble() * 360;
                particles.Add(p);
            }
        }
        return particles.ToArray();
    }

    /// <summary>程序生成临时软边透明贴图，只用作候选尘团，不导入Assets。</summary>
    private static Mesh MakeDust(Transform root)
    {
        var texture = new Texture2D(64, 64, TextureFormat.RGBA32, false); Owned.Add(texture);
        texture.wrapMode = TextureWrapMode.Clamp;
        for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
        {
            float dx = (x - 31.5f) / 31.5f, dy = (y - 31.5f) / 31.5f;
            float radius = Mathf.Sqrt(dx * dx + dy * dy);
            texture.SetPixel(x, y, new Color(1, 1, 1, Mathf.Pow(Mathf.Clamp01(1 - radius), .8f)));
        }
        texture.Apply();
        var material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")); Owned.Add(material);
        material.SetTexture("_BaseMap", texture); material.SetColor("_BaseColor", Color.white);
        material.SetFloat("_Surface", 1); material.SetFloat("_Blend", 0);
        material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0); material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); material.renderQueue = 3000;
        // 编辑器同步录制不推进ParticleSystem原生循环，临时合批面片直接投影同一粒子样本。
        var go = new GameObject("TEMP_PUFFS", typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(root, false);
        var mesh = new Mesh(); Owned.Add(mesh); go.GetComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.GetComponent<MeshRenderer>(); renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        return mesh;
    }

    /// <summary>依镜头构造临时透明面片；顶点色决定逐粒颜色与剩余透明度。</summary>
    private static void ProjectDust(Mesh mesh, Camera camera, ParticleSystem.Particle[] particles)
    {
        var vertices = new List<Vector3>(); var colors = new List<Color>(); var uv = new List<Vector2>(); var triangles = new List<int>();
        foreach (var p in particles)
        {
            int first = vertices.Count;
            var right = camera.transform.right * p.startSize * .5f; var up = camera.transform.up * p.startSize * .5f;
            vertices.Add(p.position - right - up); vertices.Add(p.position + right - up);
            vertices.Add(p.position + right + up); vertices.Add(p.position - right + up);
            colors.Add(p.startColor); colors.Add(p.startColor); colors.Add(p.startColor); colors.Add(p.startColor);
            uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(0, 1));
            triangles.AddRange(new[] { first, first + 2, first + 1, first, first + 3, first + 2 });
        }
        mesh.Clear(); mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds();
    }

    /// <summary>静止参考角色烘焙为临时网格，使同次编辑器录制仍能稳定显示皮肤。</summary>
    private static void BakeSkins(GameObject root)
    {
        foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (!skin.enabled || skin.sharedMesh == null) continue;
            var mesh = new Mesh(); Owned.Add(mesh); skin.BakeMesh(mesh);
            var go = new GameObject("TEMP_SKIN", typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(skin.transform, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh; go.GetComponent<MeshRenderer>().sharedMaterials = skin.sharedMaterials;
            skin.enabled = false;
        }
    }
}
