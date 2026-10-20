using System;
using System.IO;
using System.Collections.Generic;
using Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// 正式资源与控制器的隔离渲染对比；不保存场景、不修改Prefab、不触发Player Build。
public static class WeaponDeathImpactPreview
{
    public static string Run()
    {
        if (Application.isPlaying || Application.dataPath.Replace('\\','/') != "E:/GitHub/FirepowerLoop/Client/Assets")
            throw new Exception("只允许本工程编辑态预览");
        string frames = Path.GetFullPath("Temp/WeaponDeathImpact/frames");
        string output = Path.GetFullPath("../Docs/Plans/Artifacts/WeaponDeathImpact-20261020");
        Directory.CreateDirectory(frames); Directory.CreateDirectory(output);
        var scene = EditorSceneManager.NewPreviewScene(); var physics = scene.GetPhysicsScene();
        var owned = new List<Object>(); var views = new List<EnemyView>();
        var skins = new List<SkinnedMeshRenderer>(); var baked = new List<Mesh>();
        var oldTarget = RenderTexture.active;
        Camera camera = null; RenderTexture target = null; Texture2D pixels = null;
        GameObject New(string name) { var go=new GameObject(name); SceneManager.MoveGameObjectToScene(go,scene); return go; }
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var architecture = new global::Game.Game();
        architecture.RegisterUtility<IViewInstanceProvider>(new UnityViewInstanceProvider());
        var controller = new EnemyCtrl(architecture);
        var root = New("FormalDeathEffectsRoot");
        typeof(EnemyCtrl).GetField("arcadeRoot", flags).SetValue(controller,root.transform);
        var alive = (System.Collections.IDictionary)typeof(EnemyCtrl).GetField("arcadeEnemies", flags).GetValue(controller);
        var entryType = typeof(EnemyCtrl).GetNestedType("ArcadeEnemyPresentationEntry",System.Reflection.BindingFlags.NonPublic);
        try
        {
            EnemyPhysicalReaction.ConfigureCollisionLayers();
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(floor,scene);
            floor.transform.position=new Vector3(0,-.07f,1);floor.transform.localScale=new Vector3(30,.12f,30);
            floor.layer=LayerMask.NameToLayer(EnemyPhysicalReaction.FloorLayerName);
            floor.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Art/Arcade/Environment/Content/ForestGround.mat");
            // 地面参照线帮助区分躺倒、抛升和位移，全部为临时预览对象。
            var gridMaterial=new Material(Shader.Find("Universal Render Pipeline/Unlit"));owned.Add(gridMaterial);
            gridMaterial.color=new Color(.37f,.45f,.37f);
            for(int z=-3;z<=7;z++)
            {
                var line=New("FloorReference").AddComponent<LineRenderer>();line.sharedMaterial=gridMaterial;
                line.startWidth=line.endWidth=.012f;line.positionCount=2;line.SetPositions(new[]{new Vector3(-8,0,z),new Vector3(8,0,z)});
            }
            var key=New("ProbeKey").AddComponent<Light>();key.type=LightType.Directional;key.intensity=1.8f;key.transform.rotation=Quaternion.Euler(50,-35,0);
            camera=New("ProbeCamera").AddComponent<Camera>();camera.scene=scene;
            camera.transform.rotation=Quaternion.Euler(45,0,0);camera.transform.position=new Vector3(0,.85f,.35f)-camera.transform.forward*16;
            camera.orthographic=true;camera.orthographicSize=4.1f;camera.cullingMask=~(1<<5);
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.08f,.11f,.1f);
            for(int variant=0;variant<3;variant++)
            {
                var actor=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/ArcadeZombieShambler.prefab"));
                SceneManager.MoveGameObjectToScene(actor,scene);actor.transform.position=new Vector3((variant-1)*4f,0,-.8f);actor.transform.rotation=Quaternion.identity;
                var view=actor.GetComponent<EnemyView>();view.BeginPresentationUse();view.SetNavigationEnabled(false);
                view.Animator.Rebind();view.Animator.Update(0);views.Add(view);
                // 同一同步Editor调用内GPU蒙皮与BakeMesh均可能缓存；按当前骨矩阵更新临时网格。
                foreach(var skin in actor.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    if(!skin.enabled)continue;
                    var proxy=New("ProbeFrameSkin");proxy.transform.SetParent(skin.transform,false);
                    var bakedMesh=Object.Instantiate(skin.sharedMesh);owned.Add(bakedMesh);UpdateSkin(skin,bakedMesh);
                    proxy.AddComponent<MeshFilter>().sharedMesh=bakedMesh;
                    proxy.AddComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials;
                    skins.Add(skin);baked.Add(bakedMesh);skin.enabled=false;
                }
                foreach(var canvas in actor.GetComponentsInChildren<Canvas>(true))canvas.gameObject.SetActive(false);
                actor.transform.SetParent(root.transform,true);
                alive.Add(variant+1,Activator.CreateInstance(entryType,variant+1,"enemy.arcade.basic",view,20,actor.transform.position,0,false));
            }
            // 分隔与文字都是临时场景对象，不后期修改渲染内容。
            TextMesh Label(string text,float x,float y,int size)
            {
                var t=New("ProbeLabel").AddComponent<TextMesh>();t.text=text;t.fontSize=size;t.characterSize=.055f;
                t.anchor=TextAnchor.MiddleCenter;t.alignment=TextAlignment.Center;t.color=new Color(.91f,.93f,.9f);
                t.transform.position=camera.ViewportToWorldPoint(new Vector3(x,y,10));t.transform.rotation=camera.transform.rotation;return t;
            }
            Label("HEAVY BLAST",.17f,.93f,40); Label("ENERGY BLAST",.5f,.93f,40); Label("CLOSE SHOTGUN",.83f,.93f,40);
            var clock=Label("UNITY DEATH IMPACT PREVIEW",.5f,.07f,27);
            target=new RenderTexture(1120,700,24);camera.targetTexture=target;
            pixels=new Texture2D(1120,700,TextureFormat.RGB24,false);
            int peak=0;
            for(int frame=0;frame<64;frame++)
            {
                if(frame==10)
                {
                    for(int variant=0;variant<3;variant++)
                    {
                        controller.RemoveEnemy(variant+1,true,Vector3.forward,variant<2,false,variant);
                    }
                }
                for(int sub=0;sub<2;sub++)
                {
                    foreach(var view in views)
                    {
                        view.TickCombatFeedback(.025f);if(view.Animator.enabled)view.Animator.Update(.025f);view.LateTickCombatFeedback();
                    }
                    physics.Simulate(.025f);
                }
                var pool=typeof(EnemyCtrl).GetField("explosionDeaths",flags).GetValue(controller);
                if(pool!=null)pool.GetType().GetMethod("Tick").Invoke(pool,new object[]{.05f});
                int liveCount=0;foreach(var ps in root.GetComponentsInChildren<ParticleSystem>(true))liveCount+=ps.particleCount;
                peak=Math.Max(peak,liveCount);
                for(int i=0;i<skins.Count;i++)UpdateSkin(skins[i],baked[i]);
                clock.text=frame<10?"CURRENT ENEMY MODEL  |  SHARED SINGLE / CO-OP PRESENTATION":$"ACTUAL PREFAB + CONTROLLER   |   T + {(frame-9)*.05f:0.00}s   |   THREE DEATH IMPACT STYLES";
                camera.Render();RenderTexture.active=target;
                pixels.ReadPixels(new Rect(0,0,1120,700),0,0);pixels.Apply();
                File.WriteAllBytes(Path.Combine(frames,$"frame-{frame:D3}.png"),pixels.EncodeToPNG());
                if(frame==12)File.WriteAllBytes(Path.Combine(output,"comparison-impact.png"),pixels.EncodeToPNG());
                if(frame==18)File.WriteAllBytes(Path.Combine(output,"comparison-dissipating.png"),pixels.EncodeToPNG());
                if(frame==43)File.WriteAllBytes(Path.Combine(output,"comparison-corpse.png"),pixels.EncodeToPNG());
            }
            string report=$"Actual EnemyCtrl.RemoveEnemy + formal prefab dependency + pooled death effect. Left heavy / center energy / right close shotgun. Peak particles including existing flame feedback={peak}; 64frames at20fps; no Player Build. Frames={frames}";
            File.WriteAllText(Path.Combine(output,"probe.txt"),report);return report;
        }
        finally
        {
            RenderTexture.active=oldTarget;if(camera!=null)camera.targetTexture=null;
            controller.ReleaseAll();
            EditorSceneManager.ClosePreviewScene(scene);
            if(target!=null){target.Release();Object.DestroyImmediate(target);}if(pixels!=null)Object.DestroyImmediate(pixels);
            foreach(var asset in owned)if(asset!=null)Object.DestroyImmediate(asset);
        }
    }

    /// <summary>只用于离线预览：按实际骨骼矩阵蒙皮，避免一次Editor调用内重复渲染复用首帧姿态。</summary>
    private static void UpdateSkin(SkinnedMeshRenderer skin, Mesh output)
    {
        var source=skin.sharedMesh;var vertices=source.vertices;var normals=source.normals;
        var weights=source.boneWeights;var bind=source.bindposes;var bones=skin.bones;
        var matrices=new Matrix4x4[bones.Length];
        for(int i=0;i<bones.Length;i++)matrices[i]=skin.transform.worldToLocalMatrix*bones[i].localToWorldMatrix*bind[i];
        for(int i=0;i<vertices.Length;i++)
        {
            var w=weights[i];var v=vertices[i];var n=normals[i];
            vertices[i]=matrices[w.boneIndex0].MultiplyPoint3x4(v)*w.weight0+matrices[w.boneIndex1].MultiplyPoint3x4(v)*w.weight1+
                matrices[w.boneIndex2].MultiplyPoint3x4(v)*w.weight2+matrices[w.boneIndex3].MultiplyPoint3x4(v)*w.weight3;
            normals[i]=(matrices[w.boneIndex0].MultiplyVector(n)*w.weight0+matrices[w.boneIndex1].MultiplyVector(n)*w.weight1+
                matrices[w.boneIndex2].MultiplyVector(n)*w.weight2+matrices[w.boneIndex3].MultiplyVector(n)*w.weight3).normalized;
        }
        output.vertices=vertices;output.normals=normals;output.RecalculateBounds();
    }
}
