using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>牛撞击候选动作：只在隔离场景移动正式模型克隆，未接入战斗、未修改正式资源。</summary>
public static class HerdImpactConceptPreview
{
    public static string Run()
    {
        if (EditorApplication.isPlaying || EditorApplication.isCompiling || Application.dataPath.Replace('\\','/') != "E:/GitHub/FirepowerLoop/Client/Assets")
            throw new InvalidOperationException("只允许本工程编辑态预览");
        string output = Path.GetFullPath("Temp/HerdImpactConcept-v2-20261023/frames"); Directory.CreateDirectory(output);
        var scene = EditorSceneManager.NewPreviewScene(); var owned = new List<Object>(); var oldTarget = RenderTexture.active;
        var skins = new List<SkinnedMeshRenderer>(); var meshes = new List<Mesh>();
        Camera camera = null; RenderTexture target = null; Texture2D pixels = null;
        GameObject New(string name) { var go = new GameObject(name); SceneManager.MoveGameObjectToScene(go, scene); return go; }
        GameObject Clone(string path) { var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path)); SceneManager.MoveGameObjectToScene(go, scene); return go; }
        try
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube); SceneManager.MoveGameObjectToScene(ground,scene);
            ground.transform.position = new Vector3(1,-.08f,0); ground.transform.localScale = new Vector3(30,.14f,20);
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); owned.Add(mat); mat.color = new Color(.26f,.33f,.23f);
            ground.GetComponent<Renderer>().sharedMaterial = mat;
            var lineMat = new Material(Shader.Find("Universal Render Pipeline/Unlit")); owned.Add(lineMat); lineMat.color = new Color(.4f,.47f,.35f);
            for(int x=-8;x<=12;x++)
            {
                var line=New("GroundReference").AddComponent<LineRenderer>();line.sharedMaterial=lineMat;line.startWidth=line.endWidth=.012f;
                line.positionCount=2;line.SetPositions(new[]{new Vector3(x,.002f,-8),new Vector3(x,.002f,8)});
            }
            var sun = New("PreviewSun").AddComponent<Light>(); sun.type=LightType.Directional;sun.intensity=1.5f;sun.transform.rotation=Quaternion.Euler(48,-35,0);
            camera=New("PreviewCamera").AddComponent<Camera>();camera.scene=scene;camera.orthographic=true;camera.orthographicSize=5.8f;
            camera.transform.position=new Vector3(1,9,-14);camera.transform.LookAt(new Vector3(1,0,0));camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.08f,.11f,.1f);camera.cullingMask=~(1<<5);
            var actors = new GameObject[2]; var cows = new ArcadeHerdVisual[2]; var shadows = new Transform[2];
            for(int i=0;i<2;i++)
            {
                float z=i==0?2.7f:-2.7f;
                actors[i]=Clone(i==0?"Assets/Game/Prefabs/Characters/ArcadeZombieShambler.prefab":"Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");
                actors[i].transform.position=new Vector3(0,0,z);actors[i].transform.rotation=Quaternion.Euler(0,-90,0);
                foreach(var canvas in actors[i].GetComponentsInChildren<Canvas>(true))canvas.gameObject.SetActive(false);
                if(i==0){var view=actors[i].GetComponent<EnemyView>();view.BeginPresentationUse();view.SetNavigationEnabled(false);view.Animator.Rebind();view.Animator.Update(0);}
                else{var presenter=actors[i].GetComponent<CharacterWeaponPresenter>();presenter.SetWeaponId("arcade.weapon.basic");presenter.SetManualPoseSampling(true);presenter.BodyAnimator.Rebind();presenter.BodyAnimator.Update(0);presenter.Current.Animations.Idle.Clip.SampleAnimation(presenter.BodyAnimator.gameObject,.1f);presenter.ApplyOffhandGrip(presenter.Current.Animations.Idle);}
                foreach(var skin in actors[i].GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    if(!skin.enabled)continue;
                    var proxy=New("TemporarySkin");proxy.transform.SetParent(skin.transform,false);
                    var mesh=Object.Instantiate(skin.sharedMesh);owned.Add(mesh);UpdateSkin(skin,mesh);
                    proxy.AddComponent<MeshFilter>().sharedMesh=mesh;proxy.AddComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials;
                    skins.Add(skin);meshes.Add(mesh);skin.enabled=false;
                }
                cows[i]=Clone("Assets/Game/Prefabs/SceneObjects/ArcadeHerdVisual.prefab").GetComponent<ArcadeHerdVisual>();
                cows[i].Bind(999,i+1);cows[i].transform.rotation=Quaternion.Euler(0,90,0);
                var shadow=GameObject.CreatePrimitive(PrimitiveType.Cylinder);SceneManager.MoveGameObjectToScene(shadow,scene);
                shadow.transform.localScale=new Vector3(.75f,.004f,.75f);shadow.GetComponent<Renderer>().sharedMaterial=lineMat;shadows[i]=shadow.transform;
            }
            target=new RenderTexture(1000,700,24);camera.targetTexture=target;pixels=new Texture2D(1000,700,TextureFormat.RGB24,false);
            var pose=typeof(ArcadeHerdVisual).GetMethod("Pose",BindingFlags.Instance|BindingFlags.NonPublic);
            for(int frame=0;frame<80;frame++)
            {
                float time=frame*.05f; float sinceHit=time-.65f; float flight=Mathf.Clamp01(sinceHit/.95f);
                // 落地时刻为1.60秒，之后完整停顿0.50秒，最后用0.25秒演示恢复站立。
                float recovery=Mathf.Clamp01((sinceHit-.95f-.5f)/.25f);
                for(int i=0;i<2;i++)
                {
                    float z=i==0?2.7f:-2.7f; float x=3f*flight; float height=2.5f*4f*flight*(1f-flight);
                    // 两者均演示存活受击：身体完整离地，落地趴住0.5秒再恢复；不模拟正式伤害。
                    float tilt=sinceHit<=0?0:-85f*Mathf.Clamp01(flight*3)*(1f-Mathf.SmoothStep(0,1,recovery));
                    actors[i].transform.position=new Vector3(x,height+.12f*flight*(1f-recovery),z);
                    actors[i].transform.rotation=Quaternion.AngleAxis(tilt,Vector3.forward)*Quaternion.Euler(0,-90,0);
                    shadows[i].position=new Vector3(x,.005f,z);
                    cows[i].transform.position=new Vector3(-1.5f+7.15f*(time-.65f),0,z);pose.Invoke(cows[i],new object[]{true,time});
                }
                for(int i=0;i<skins.Count;i++)UpdateSkin(skins[i],meshes[i]);
                camera.Render();RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1000,700),0,0);pixels.Apply();
                File.WriteAllBytes(Path.Combine(output,$"frame-{frame:D3}.png"),pixels.EncodeToPNG());
            }
            return "v2临时动作候选；正式模型克隆，未接入正式碰撞。80帧20fps，前飞3m，抛升2.5m，腾空0.95s，落地僵直0.50s，恢复起身0.25s。两者演示存活受击；击杀仍保持死亡。";
        }
        finally
        {
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
