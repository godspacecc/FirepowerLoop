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

/// <summary>玩家侧飞候选：完整五牛纵队经过侧边落点；仅临时模型克隆和轨迹，不改正式规则。</summary>
public static class HerdImpactSidePreview
{
    public static string Run()
    {
        if (EditorApplication.isPlaying || EditorApplication.isCompiling || Application.dataPath.Replace('\\','/') != "E:/GitHub/FirepowerLoop/Client/Assets")
            throw new InvalidOperationException("只允许本工程编辑态预览");
        string output = Path.GetFullPath("Temp/HerdImpactSide-v3-20261023/frames"); Directory.CreateDirectory(output);
        var scene = EditorSceneManager.NewPreviewScene(); var owned = new List<Object>(); var oldTarget = RenderTexture.active;
        var skins = new List<SkinnedMeshRenderer>(); var meshes = new List<Mesh>();
        Camera camera = null; RenderTexture target = null; Texture2D pixels = null;
        GameObject New(string name) { var go = new GameObject(name); SceneManager.MoveGameObjectToScene(go, scene); return go; }
        GameObject Clone(string path) { var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path)); SceneManager.MoveGameObjectToScene(go, scene); return go; }
        try
        {
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
                float time=frame*.05f,sinceHit=time-.65f,flight=Mathf.Clamp01(sinceHit/.95f);
                float recovery=Mathf.Clamp01((sinceHit-.95f-.5f)/.25f);
                // 只在预览中示范斜向侧边3米：前移1.2米、横移约2.75米；正式动作尚待采用。
                var direction=new Vector3(.4f,0,Mathf.Sqrt(.84f));
                float height=2.5f*4*flight*(1-flight)+.12f*flight*(1-recovery);
                float tilt=85f*Mathf.Clamp01(flight*3)*(1-Mathf.SmoothStep(0,1,recovery));
                actor.transform.position=direction*3*flight+Vector3.up*height;
                actor.transform.rotation=Quaternion.AngleAxis(tilt,Vector3.Cross(Vector3.up,direction))*Quaternion.Euler(0,-90,0);
                shadow.transform.position=direction*3*flight+Vector3.up*.005f;
                for(int i=0;i<cows.Length;i++)
                {
                    cows[i].transform.position=new Vector3(-1.55f+7.15f*(time-.65f)-3*i,0,0);
                    pose.Invoke(cows[i],new object[]{true,time});
                }
                for(int i=0;i<skins.Count;i++)UpdateSkin(skins[i],meshes[i]);
                camera.Render();RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1000,700),0,0);pixels.Apply();
                File.WriteAllBytes(Path.Combine(output,$"frame-{frame:D3}.png"),pixels.EncodeToPNG());
            }
            return "v3侧飞临时动作候选：100帧20fps，五牛单列间距3m、7.15m/s；玩家斜侧飞3m（前1.2m、横2.75m），腾空0.95s、抛升2.5m、落地停0.5s、恢复0.25s。仅正式模型克隆，不改正式运行时及资源。";
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
