using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.Playables;
using UnityEngine.Animations;

/// <summary>构建独立穿戴美术样例，验证骨架与动画；不修改正式玩家或背包数据。</summary>
public static class PlayerOutfitPrototypeAuthoring
{
    const string Folder = "Assets/Game/Art/Player/OutfitPrototype";
    const string Original = "Assets/Game/Art/Player/Humanoid/PlayerCityCharacters.fbx";
    const string Prefab = Folder + "/PlayerOutfitPreview.prefab";

    /// <summary>将 Blender 派生网格绑定回正式骨架，重复执行更新同一批样例资产。</summary>
    public static string Create()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var original = AssetDatabase.LoadAssetAtPath<GameObject>(Original);
        var derived = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/PlayerModularOutfit.fbx");
        var root = UnityEngine.Object.Instantiate(original);
        root.name = "PlayerOutfitPreview";
        try
        {
            var bones = root.GetComponentsInChildren<Transform>(true).ToDictionary(t => t.name, t => t);
            var originalRenderer = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(r => r.name.Contains("Hoodie"));
            var originalBones = originalRenderer.bones;
            var originalBindposes = originalRenderer.sharedMesh.bindposes;
            var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Art/Player/Humanoid/PlayerCity.mat");
            foreach (var r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) UnityEngine.Object.DestroyImmediate(r.gameObject);
            var report = new List<string>();
            foreach (var src in derived.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var obj = new GameObject(src.name); obj.transform.SetParent(root.transform, false);
                var dst = obj.AddComponent<SkinnedMeshRenderer>();
                dst.bones = src.bones.Select(b => bones[b.name.Replace(".001", " 1")]).ToArray();
                // 显式重建顶点流，不复制 FBX 导入网格的内部 GPU 缓冲布局。
                var input = src.sharedMesh;
                var mesh = new Mesh { name = src.name, indexFormat = input.indexFormat };
                mesh.vertices = input.vertices; mesh.normals = input.normals; mesh.tangents = input.tangents;
                mesh.uv = input.uv; mesh.boneWeights = input.boneWeights; mesh.triangles = input.triangles;
                mesh.RecalculateBounds();
                // 沿用原始绑定矩阵，避免 FBX 往返导入的骨轴差异影响既有 Humanoid 动画。
                mesh.bindposes = dst.bones.Select(b => originalBindposes[Array.IndexOf(originalBones, b)]).ToArray();
                var path = Folder + "/" + src.name + ".asset";
                var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                // 网格顶点布局会随 Blender 修改变化；完整重建本工具独占的样例资产，避免旧 GPU 蒙皮缓冲残留。
                if (existing != null) AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(mesh, path);
                dst.sharedMesh = mesh; dst.sharedMaterial = material; dst.rootBone = bones["Root"];
                dst.localBounds = new Bounds(Vector3.up, new Vector3(3,3,3)); dst.updateWhenOffscreen = true;
                obj.SetActive(src.name != "Top_OliveUtility" && src.name != "Pants_TanCargo");
                if (dst.bones.Any(b => b == null) || mesh.boneWeights.Any(w => w.weight0 + w.weight1 + w.weight2 + w.weight3 < .99f)) throw new Exception("骨骼或权重缺失: " + src.name);
                report.Add(src.name + ": " + mesh.vertexCount + " vertices, " + dst.bones.Length + " bones");
            }
            var animator = root.GetComponent<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman) throw new Exception("原角色 Avatar 不可用");
            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Game/Art/Player/Humanoid/PlayerHumanoid.controller");
            PrefabUtility.SaveAsPrefabAsset(root, Prefab); AssetDatabase.SaveAssets();
            return string.Join("\n", report) + "\nAvatar valid; prefab saved: " + Prefab;
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    /// <summary>在独立预览场景采样既有动画并输出图片，不改变当前已打开场景。</summary>
    public static string Render()
    {
        var output = Path.GetFullPath("../Tools/Blender/OutfitPreview"); Directory.CreateDirectory(output);
        var preview = new PreviewRenderUtility();
        var graphs = new List<PlayableGraph>();
        var diagnostics = new List<string>();
        try
        {
            preview.camera.orthographic = true; preview.camera.orthographicSize = 1.12f;
            preview.camera.nearClipPlane = .01f; preview.camera.farClipPlane = 30;
            preview.camera.backgroundColor = new Color(.105f,.125f,.15f); preview.camera.clearFlags = CameraClearFlags.SolidColor;
            preview.camera.transform.position = new Vector3(0,1.05f,6);
            preview.camera.transform.LookAt(new Vector3(0,.95f,0));
            preview.lights[0].intensity = 1.4f; preview.lights[0].transform.rotation = Quaternion.Euler(35,145,0);
            preview.lights[1].intensity = .8f; preview.ambientColor = new Color(.55f,.55f,.55f);
            var actors = new List<GameObject>();
            var baked = new Dictionary<SkinnedMeshRenderer, Mesh>();
            for (int i=0;i<4;i++)
            {
                var root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab)); preview.AddSingleGO(root);
                foreach(var r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    r.gameObject.SetActive(r.name.StartsWith("Body") || r.name == (i<2?"Top_RedHoodie":"Top_OliveUtility") || r.name == (i%2==0?"Pants_BlueDenim":"Pants_TanCargo"));
                root.transform.position = new Vector3((i-1.5f)*1.05f,0,0); root.transform.rotation = Quaternion.Euler(0,-15,0);
                actors.Add(root);
                foreach(var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    var display = new GameObject("Sample_"+skin.name); display.transform.SetParent(skin.transform,false);
                    var mesh = new Mesh(); baked.Add(skin,mesh); display.AddComponent<MeshFilter>().sharedMesh=mesh;
                    display.AddComponent<MeshRenderer>().sharedMaterial=skin.sharedMaterial;
                    skin.enabled=false;
                }
            }
            foreach (var mode in new[]{"Idle", "Walk", "Run"})
            {
                var path = mode=="Idle" ? "Assets/Game/Art/Player/Animations/JUTPS/Imported/JUTPSRifleIdle.fbx" : "Assets/Game/Art/Player/Animations/Mixamo/Imported/Standard"+mode+".fbx";
                var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c=>!c.name.StartsWith("__"));
                foreach (var actor in actors)
                {
                    var animator=actor.GetComponent<Animator>(); animator.runtimeAnimatorController=null; animator.cullingMode=AnimatorCullingMode.AlwaysAnimate; animator.applyRootMotion=false;
                    var graph=PlayableGraph.Create("OutfitPreview"); graphs.Add(graph); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                    var playable=AnimationClipPlayable.Create(graph,clip); var animOutput=AnimationPlayableOutput.Create(graph,"Pose",animator); animOutput.SetSourcePlayable(playable); graph.Play();
                }
                int frameCount=mode=="Idle"?1:16;
                for(int f=0; f<frameCount; f++)
                {
                    for(int i=0;i<graphs.Count;i++)
                    {
                        var playable=graphs[i].GetRootPlayable(0); playable.SetTime(clip.length*f/frameCount); graphs[i].Evaluate(.0001f);
                        actors[i].transform.position=new Vector3((i-1.5f)*1.05f,0,0); actors[i].transform.rotation=Quaternion.Euler(0,-15,0);
                    }
                    foreach(var pair in baked) { pair.Key.BakeMesh(pair.Value); pair.Value.RecalculateBounds(); }
                    preview.BeginStaticPreview(new Rect(0,0,1600,760)); preview.Render(true);
                    if(f==0 || f==6) diagnostics.Add(mode+" "+f+" scale="+actors[0].transform.localScale+" hips="+actors[0].GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Hips).position+" bounds="+actors[0].GetComponentInChildren<SkinnedMeshRenderer>().bounds);
                    var tex=preview.EndStaticPreview(); File.WriteAllBytes(Path.Combine(output,mode+"_"+f.ToString("D2")+".png"),tex.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(tex);
                }
                foreach(var graph in graphs) graph.Destroy(); graphs.Clear();
            }
            foreach(var mesh in baked.Values) UnityEngine.Object.DestroyImmediate(mesh);
            return output+"\n"+string.Join("\n",diagnostics);
        }
        finally { foreach(var graph in graphs) if(graph.IsValid()) graph.Destroy(); preview.Cleanup(); }
    }
}
