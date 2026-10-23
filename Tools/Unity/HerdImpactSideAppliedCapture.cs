using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using FirepowerLoop.Arcade;
using Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>正式侧飞接入隔离录制：真实五牛接触、权威侧移与正式主角View同源，不保存场景。</summary>
public static class HerdImpactSideAppliedCapture
{
    public static string Run()
    {
        if (EditorApplication.isPlaying || EditorApplication.isCompiling || Application.dataPath.Replace('\\','/') != "E:/GitHub/FirepowerLoop/Client/Assets")
            throw new InvalidOperationException("只允许本工程编辑态预览");
        string output = Path.GetFullPath("Temp/HerdImpactSideApplied-20261023/frames"); Directory.CreateDirectory(output);
        var scene = EditorSceneManager.NewPreviewScene(); var owned = new List<Object>(); var oldTarget = RenderTexture.active;
        var skins = new List<SkinnedMeshRenderer>(); var meshes = new List<Mesh>();
        Camera camera = null; RenderTexture target = null; Texture2D pixels = null;
        GameObject New(string name) { var go = new GameObject(name); SceneManager.MoveGameObjectToScene(go, scene); return go; }
        GameObject Clone(string path) { var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path)); SceneManager.MoveGameObjectToScene(go, scene); return go; }
        object fixture=null;Type fixtureType=null;
        try
        {
            const BindingFlags hidden=BindingFlags.Instance|BindingFlags.NonPublic;
            fixtureType=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("Game.Tests.ArcadeHerdIntegrationTests")).First(t=>t!=null);
            fixture=Activator.CreateInstance(fixtureType);fixtureType.GetMethod("Setup").Invoke(fixture,null);
            var system=fixtureType.GetField("system",hidden).GetValue(fixture);
            var runtime=fixtureType.GetField("runtime",hidden).GetValue(fixture);var runtimeType=runtime.GetType();
            var tick=system.GetType().GetMethod("TickRoundContent",hidden);tick.Invoke(system,new[]{runtime,(object)8f});
            var herd=(List<ArcadeHerdState>)runtimeType.GetField("HerdCows").GetValue(runtime);
            var trace=new List<string>{"frame,remaining,playerX,playerZ,health,headX,tailX"};
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube); SceneManager.MoveGameObjectToScene(ground,scene);
            ground.transform.position = new Vector3(0,-.08f,0); ground.transform.localScale = new Vector3(44,.14f,24);
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); owned.Add(mat); mat.color = new Color(.26f,.33f,.23f);
            ground.GetComponent<Renderer>().sharedMaterial = mat;
            var lineMat = new Material(Shader.Find("Universal Render Pipeline/Unlit")); owned.Add(lineMat); lineMat.color = new Color(.4f,.47f,.35f);
            for(int x=-20;x<=22;x++)
            {
                var line=New("GroundReference").AddComponent<LineRenderer>();line.sharedMaterial=lineMat;line.startWidth=line.endWidth=.012f;
                line.positionCount=2;line.SetPositions(new[]{new Vector3(x,.002f,-10),new Vector3(x,.002f,10)});
            }
            var sun = New("PreviewSun").AddComponent<Light>(); sun.type=LightType.Directional;sun.intensity=1.5f;sun.transform.rotation=Quaternion.Euler(48,-35,0);
            camera=New("PreviewCamera").AddComponent<Camera>();camera.scene=scene;camera.orthographic=true;camera.orthographicSize=7.5f;
            camera.transform.position=new Vector3(-1,13,-18);camera.transform.LookAt(new Vector3(-1,0,0));camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.08f,.11f,.1f);camera.cullingMask=~(1<<5);
            // 完整五牛前后纵队，只演示一个正式主角，避免并列分道被误认为实际队形。
            var actor=Clone("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");
            actor.transform.rotation=Quaternion.Euler(0,-90,0);
            foreach(var canvas in actor.GetComponentsInChildren<Canvas>(true))canvas.gameObject.SetActive(false);
            var presenter=actor.GetComponent<CharacterWeaponPresenter>();presenter.SetWeaponId("arcade.weapon.basic");presenter.SetManualPoseSampling(true);presenter.BodyAnimator.Rebind();presenter.BodyAnimator.Update(0);presenter.Current.Animations.Idle.Clip.SampleAnimation(presenter.BodyAnimator.gameObject,.1f);presenter.ApplyOffhandGrip(presenter.Current.Animations.Idle);
            foreach(var skin in actor.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if(!skin.enabled)continue;
                var proxy=New("TemporarySkin");proxy.transform.SetParent(skin.transform,false);
                var mesh=Object.Instantiate(skin.sharedMesh);owned.Add(mesh);UpdateSkin(skin,mesh);
                proxy.AddComponent<MeshFilter>().sharedMesh=mesh;proxy.AddComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials;
                skins.Add(skin);meshes.Add(mesh);skin.enabled=false;
            }
            var cows=new ArcadeHerdVisual[5];
            for(int i=0;i<cows.Length;i++)
            {
                cows[i]=Clone("Assets/Game/Prefabs/SceneObjects/ArcadeHerdVisual.prefab").GetComponent<ArcadeHerdVisual>();
                cows[i].Bind(999,i+1);cows[i].transform.rotation=Quaternion.Euler(0,90,0);
            }
            var shadow=GameObject.CreatePrimitive(PrimitiveType.Cylinder);SceneManager.MoveGameObjectToScene(shadow,scene);
            shadow.transform.localScale=new Vector3(.75f,.004f,.75f);shadow.GetComponent<Renderer>().sharedMaterial=lineMat;
            var laneMat=new Material(Shader.Find("Universal Render Pipeline/Unlit"));owned.Add(laneMat);laneMat.color=new Color(.65f,.57f,.25f);
            foreach(float edge in new[]{-1.1f,1.1f})
            {
                var line=New("TemporaryCowRouteBoundary").AddComponent<LineRenderer>();line.sharedMaterial=laneMat;line.startWidth=line.endWidth=.035f;line.positionCount=2;
                line.SetPositions(new[]{new Vector3(-22,.005f,edge),new Vector3(22,.005f,edge)});
            }
            target=new RenderTexture(1000,700,24);camera.targetTexture=target;pixels=new Texture2D(1000,700,TextureFormat.RGB24,false);
            var pose=typeof(ArcadeHerdVisual).GetMethod("Pose",BindingFlags.Instance|BindingFlags.NonPublic);
            for(int frame=0;frame<100;frame++)
            {
                // 与定向回归一致，撤掉临时保护以证明后牛落空来自实际侧移；正式保护规则不修改。
                runtimeType.GetField("PlayerInvulnerability").SetValue(runtime,0f);
                if(frame>0)tick.Invoke(system,new[]{runtime,(object).05f});
                float time=frame*.05f;
                var position=(Vector2)runtimeType.GetField("PlayerPosition").GetValue(runtime);
                var state=(ArcadeHerdImpactState)runtimeType.GetField("HerdImpact").GetValue(runtime);
                int health=(int)runtimeType.GetField("PlayerHealth").GetValue(runtime);
                trace.Add($"{frame},{state.Remaining:R},{position.x:R},{position.y:R},{health},{herd[0].X:R},{herd[4].X:R}");
                actor.transform.position=new Vector3(position.x+26,0,position.y+14);
                actor.transform.rotation=Quaternion.Euler(0,-90,0);
                var playerView=actor.GetComponent<PlayerView>();
                playerView.SetHerdImpact(state,new Vector3(state.DirectionX,0,state.DirectionZ));
                typeof(PlayerView).GetMethod("LateUpdate",hidden).Invoke(playerView,null);
                shadow.transform.position=new Vector3(position.x+26,.005f,position.y+14);
                for(int i=0;i<cows.Length;i++)
                {
                    var snapshot=ArcadeHerdRules.Snapshot(herd[i],new ArcadeHerdTuning(.8f,7.15f,78,.65f));
                    cows[i].transform.position=new Vector3(snapshot.X+26,0,snapshot.Z+14);
                    pose.Invoke(cows[i],new object[]{snapshot.Phase==ArcadeHerdPhase.Charging,(float)snapshot.PhaseSeconds});
                }
                for(int i=0;i<skins.Count;i++)UpdateSkin(skins[i],meshes[i]);
                camera.Render();RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1000,700),0,0);pixels.Apply();
                File.WriteAllBytes(Path.Combine(output,$"frame-{frame:D3}.png"),pixels.EncodeToPNG());
            }
            File.WriteAllLines(Path.Combine(Path.GetDirectoryName(output),"authority-trace.csv"),trace);
            return "正式侧飞：100帧20fps完整五牛纵队；真实接触与活动步推进侧移、阶段和生命，正式PlayerView投影。隔离录制，不保存场景。";
        }
        finally
        {
            fixtureType?.GetMethod("Cleanup")?.Invoke(fixture,null);
            RenderTexture.active=oldTarget;if(camera!=null)camera.targetTexture=null;EditorSceneManager.ClosePreviewScene(scene);
            if(target!=null){target.Release();Object.DestroyImmediate(target);}if(pixels!=null)Object.DestroyImmediate(pixels);
            foreach(var item in owned)if(item!=null)Object.DestroyImmediate(item);
        }
    }

    /// <summary>同步录制时按骨骼矩阵计算临时蒙皮，避免同次Editor调用复用首帧缓存。</summary>
    private static void UpdateSkin(SkinnedMeshRenderer skin,Mesh output)
    {
        var source=skin.sharedMesh;var vertices=source.vertices;var normals=source.normals;var weights=source.boneWeights;var bind=source.bindposes;var bones=skin.bones;
        var matrices=new Matrix4x4[bones.Length];for(int i=0;i<bones.Length;i++)matrices[i]=skin.transform.worldToLocalMatrix*bones[i].localToWorldMatrix*bind[i];
        for(int i=0;i<vertices.Length;i++)
        {
            var w=weights[i];var v=vertices[i];var n=normals[i];
            vertices[i]=matrices[w.boneIndex0].MultiplyPoint3x4(v)*w.weight0+matrices[w.boneIndex1].MultiplyPoint3x4(v)*w.weight1+matrices[w.boneIndex2].MultiplyPoint3x4(v)*w.weight2+matrices[w.boneIndex3].MultiplyPoint3x4(v)*w.weight3;
            normals[i]=(matrices[w.boneIndex0].MultiplyVector(n)*w.weight0+matrices[w.boneIndex1].MultiplyVector(n)*w.weight1+matrices[w.boneIndex2].MultiplyVector(n)*w.weight2+matrices[w.boneIndex3].MultiplyVector(n)*w.weight3).normalized;
        }
        output.vertices=vertices;output.normals=normals;output.RecalculateBounds();
    }
}
