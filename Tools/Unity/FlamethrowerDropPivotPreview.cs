using System;
using System.IO;
using System.Linq;
using Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

/// <summary>空投喷火器旋转中心的隔离对比；Run不保存资源，已采用姿态由独立入口接入。</summary>
public static class FlamethrowerDropPivotPreview
{
    public static string Run()=>Render(false);
    /// <summary>右侧直接加载正式姿态；左侧仅在临时实例恢复旧零偏移，便于同机位对照。</summary>
    public static string RunApplied()=>Render(true);
    private static string Render(bool applied)
    {
        if(EditorApplication.isPlaying||EditorApplication.isCompiling||Application.dataPath.Replace('\\','/')!="E:/GitHub/FirepowerLoop/Client/Assets")
            throw new InvalidOperationException("仅本工程编辑态制作隔离预览");
        var prior=SceneManager.GetActiveScene();var priorRT=RenderTexture.active;
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        RenderTexture target=null;Texture2D pixels=null;Material floorMaterial=null;
        try
        {
            SceneManager.SetActiveScene(scene);RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.62f,.64f,.65f);
            var pickups=new GameObject[2];
            for(int index=0;index<2;index++)
            {
                var go=Object.Instantiate(Load("SceneObjects/TestResourcePickup"));pickups[index]=go;go.transform.position=new Vector3(index==0?-2.1f:2.1f,.3f,0);
                var pickup=go.GetComponent<ResourcePickupView>();
                if(applied&&index==0)
                {
                    var serialized=new SerializedObject(pickup);var poses=serialized.FindProperty("weaponDeliveryPoses");
                    for(int p=0;p<poses.arraySize;p++)if(poses.GetArrayElementAtIndex(p).FindPropertyRelative("prefabAddress").stringValue=="Prefabs/SceneObjects/ArcadeFlamethrower")
                        poses.GetArrayElementAtIndex(p).FindPropertyRelative("localPosition").vector3Value=Vector3.zero;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                pickup.SetArcadePresentation(Load("SceneObjects/ArcadeFlamethrower"),"Prefabs/SceneObjects/ArcadeFlamethrower");
                pickup.SetWeaponDelivery(true,0,1.2f);pickup.SetNameVisible(false);
                if(index==1)
                {
                    // 候选只补偿水平中心；底面高度、枪体姿态与尺寸保持正式版。
                    var visual=go.transform.Find("ArcadeDropVisual");var b=BoundsInPickup(go.transform,visual);
                    if(!applied)visual.localPosition-=new Vector3(b.center.x,0,b.center.z);
                    var centered=BoundsInPickup(go.transform,visual);
                    if(new Vector2(centered.center.x,centered.center.z).magnitude>.0001f)throw new InvalidOperationException("候选未对准中心");
                }
            }
            var hero=Object.Instantiate(Load("Characters/GrayboxPlayer"));hero.transform.position=new Vector3(0,0,1.35f);hero.transform.rotation=Quaternion.Euler(0,180,0);
            var presenter=hero.GetComponent<CharacterWeaponPresenter>();if(!presenter.SetWeaponId("arcade.weapon.basic"))throw new InvalidOperationException(presenter.LastError);
            presenter.SetManualPoseSampling(true);presenter.BodyAnimator.Rebind();presenter.BodyAnimator.Update(0);presenter.Current.Animations.Idle.Clip.SampleAnimation(presenter.BodyAnimator.gameObject,.1f);presenter.ApplyOffhandGrip(presenter.Current.Animations.Idle);
            foreach(var canvas in hero.GetComponentsInChildren<Canvas>(true))canvas.enabled=false;
            floorMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit")){color=new Color(.23f,.30f,.31f)};
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.position=new Vector3(0,-.12f,0);floor.transform.localScale=new Vector3(30,.15f,30);floor.GetComponent<Renderer>().sharedMaterial=floorMaterial;Object.DestroyImmediate(floor.GetComponent<Collider>());
            foreach(var root in scene.GetRootGameObjects())foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=30;
            var light=new GameObject("PreviewSun",typeof(Light)).GetComponent<Light>();light.type=LightType.Directional;light.intensity=1.5f;light.shadows=LightShadows.Soft;light.cullingMask=1<<30;light.transform.rotation=Quaternion.Euler(50,-35,0);
            var camera=new GameObject("PreviewCamera",typeof(Camera)).GetComponent<Camera>();camera.cullingMask=1<<30;camera.orthographic=true;camera.orthographicSize=2.8f;camera.nearClipPlane=.1f;camera.farClipPlane=100;
            camera.transform.position=new Vector3(0,7,-9);camera.transform.LookAt(new Vector3(0,.8f,.1f));camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.10f,.16f,.18f);
            target=new RenderTexture(1120,630,24);pixels=new Texture2D(1120,630,TextureFormat.RGB24,false);camera.targetTexture=target;
            string folder=Path.GetFullPath(applied?"Temp/FlamethrowerDropPivotApplied":"Temp/FlamethrowerDropPivotPreview");Directory.CreateDirectory(folder);
            for(int frame=0;frame<80;frame++)
            {
                float seconds=frame/20f;
                foreach(var go in pickups)go.transform.rotation=Quaternion.Euler(0,seconds*90,0);
                foreach(var root in pickups)foreach(var ps in root.GetComponentsInChildren<ParticleSystem>(true))
                    if(ps.gameObject.activeInHierarchy){ps.Simulate(seconds,false,true,false);ps.Pause();}
                camera.Render();RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1120,630),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(folder,frame.ToString("D3")+".png"),pixels.EncodeToPNG());
            }
            camera.targetTexture=null;return folder;
        }
        finally
        {
            RenderTexture.active=priorRT;if(pixels!=null)Object.DestroyImmediate(pixels);if(target!=null){target.Release();Object.DestroyImmediate(target);}
            SceneManager.SetActiveScene(prior);EditorSceneManager.CloseScene(scene,true);if(floorMaterial!=null)Object.DestroyImmediate(floorMaterial);
        }
    }
    /// <summary>用户采用后仅更新现有空投姿态位置；模型、角度、尺寸、普通掉落和手持资源均保持。</summary>
    public static string ApplyApprovedPose()
    {
        if(EditorApplication.isPlaying||EditorApplication.isCompiling||Application.dataPath.Replace('\\','/')!="E:/GitHub/FirepowerLoop/Client/Assets")
            throw new InvalidOperationException("仅本工程编辑态接入已采用姿态");
        Vector3 offset;
        var sample=Object.Instantiate(Load("SceneObjects/TestResourcePickup"));
        try
        {
            var source=Load("SceneObjects/ArcadeFlamethrower");var pickup=sample.GetComponent<ResourcePickupView>();
            pickup.SetArcadePresentation(source,"Prefabs/SceneObjects/ArcadeFlamethrower");pickup.SetWeaponDelivery(true,0,1.2f);
            var visual=sample.transform.Find("ArcadeDropVisual");var bounds=BoundsInPickup(sample.transform,visual);
            float fitScale=visual.localScale.x/source.transform.localScale.x;
            if(fitScale<=.0001f)throw new InvalidOperationException("空投缩放无效");
            offset=new Vector3(bounds.center.x,0,bounds.center.z)/fitScale;
        }
        finally{Object.DestroyImmediate(sample);}
        const string path="Assets/Game/Prefabs/SceneObjects/TestResourcePickup.prefab";
        var root=PrefabUtility.LoadPrefabContents(path);
        try
        {
            var serialized=new SerializedObject(root.GetComponent<ResourcePickupView>());var poses=serialized.FindProperty("weaponDeliveryPoses");
            for(int i=0;i<poses.arraySize;i++)
            {
                var pose=poses.GetArrayElementAtIndex(i);
                if(pose.FindPropertyRelative("prefabAddress").stringValue!="Prefabs/SceneObjects/ArcadeFlamethrower")continue;
                var position=pose.FindPropertyRelative("localPosition");position.vector3Value-=offset;
                var result=position.vector3Value;serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root,path);return "已保存已确认空投中心姿态："+result;
            }
            throw new InvalidOperationException("正式拾取模板缺少喷火器空投姿态");
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
    }
    /// <summary>实体网格包围盒转换至拾取根；枪口粒子不属于武器实体。</summary>
    private static Bounds BoundsInPickup(Transform pickup,Transform visual)
    {
        var bounds=new Bounds();bool found=false;
        foreach(var renderer in visual.GetComponentsInChildren<Renderer>(true).Where(r=>!(r is ParticleSystemRenderer)))
        {
            var b=renderer.localBounds;var matrix=pickup.worldToLocalMatrix*renderer.transform.localToWorldMatrix;
            for(int corner=0;corner<8;corner++)
            {
                var point=matrix.MultiplyPoint3x4(b.center+Vector3.Scale(b.extents,new Vector3((corner&1)==0?-1:1,(corner&2)==0?-1:1,(corner&4)==0?-1:1)));
                if(found)bounds.Encapsulate(point);else{bounds=new Bounds(point,Vector3.zero);found=true;}
            }
        }
        if(!found)throw new InvalidOperationException("喷火器缺少实体网格");return bounds;
    }
    private static GameObject Load(string path)=>AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/"+path+".prefab")??throw new InvalidOperationException(path);
}
