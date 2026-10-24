using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>仅在隔离临时场景比较已有步枪和无人机，不保存或改写正式资源。</summary>
public static class IslandRevisionAssetPreview
{
    public static string Run()
    {
        if (EditorApplication.isPlaying || EditorApplication.isCompiling) throw new InvalidOperationException("仅编辑态预览");
        for (int i=0;i<SceneManager.sceneCount;i++) if(SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("请先处理脏场景");
        var priorScene=SceneManager.GetActiveScene(); var priorRt=RenderTexture.active;
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        RenderTexture rt=null; Texture2D image=null; Material floor=null;
        try
        {
            SceneManager.SetActiveScene(scene);
            var root=new GameObject("ExistingAssets_TemporaryPreview");
            var shader=Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            floor=new Material(shader); floor.color=new Color(.12f,.16f,.20f);
            var ground=GameObject.CreatePrimitive(PrimitiveType.Cube);ground.transform.SetParent(root.transform);
            ground.transform.position=new Vector3(0,-.12f,.2f);ground.transform.localScale=new Vector3(9,.2f,4);
            ground.GetComponent<Renderer>().sharedMaterial=floor;
            Place("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab",new Vector3(-2.5f,0,0),0,false,root.transform);
            Place("Assets/Game/Art/Arcade/Weapons/ArcadeRifle.fbx",new Vector3(-.4f,.85f,0),.9f,true,root.transform);
            Place("Assets/Game/Prefabs/SceneObjects/ArcadeRifle.prefab",new Vector3(1.5f,.85f,0),0,true,root.transform);
            Place("Assets/Game/Prefabs/SceneObjects/ArcadeSupportDroneVisual.prefab",new Vector3(3f,1,0),0,false,root.transform);
            var key=new GameObject("PreviewKey",typeof(Light)).GetComponent<Light>();key.type=LightType.Directional;
            key.intensity=1.5f;key.transform.rotation=Quaternion.Euler(45,-25,0);
            var fill=new GameObject("PreviewFill",typeof(Light)).GetComponent<Light>();fill.type=LightType.Directional;
            fill.intensity=.7f;fill.transform.rotation=Quaternion.Euler(35,145,0);
            var camera=new GameObject("PreviewCamera",typeof(Camera)).GetComponent<Camera>();camera.orthographic=true;
            camera.orthographicSize=2.15f;camera.transform.position=new Vector3(0,3.1f,-7.5f);camera.transform.LookAt(new Vector3(0,.85f,0));
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.06f,.08f,.11f);
            foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;
            camera.cullingMask=1<<31; key.cullingMask=1<<31;fill.cullingMask=1<<31;
            rt=new RenderTexture(1600,800,24);image=new Texture2D(1600,800,TextureFormat.RGB24,false);
            camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
            image.ReadPixels(new Rect(0,0,1600,800),0,0);image.Apply();
            var path=Path.GetFullPath("../Docs/Plans/Artifacts/IslandRevision-20261021/existing-assets.png");
            File.WriteAllBytes(path,image.EncodeToPNG());camera.targetTexture=null;
            return path;
        }
        finally
        {
            RenderTexture.active=priorRt;
            if(image!=null)Object.DestroyImmediate(image);if(rt!=null)Object.DestroyImmediate(rt);if(floor!=null)Object.DestroyImmediate(floor);
            SceneManager.SetActiveScene(priorScene);EditorSceneManager.CloseScene(scene,true);
        }
    }
    /// <summary>保留正式Prefab比例；未接入的步枪源模型仅按0.9米候选长度展示。</summary>
    private static void Place(string path,Vector3 position,float length,bool horizontal,Transform root)
    {
        var asset=AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if(asset==null)throw new InvalidOperationException("缺少候选资源："+path);
        var go=Object.Instantiate(asset,root);go.name=Path.GetFileNameWithoutExtension(path);
        foreach(var behaviour in go.GetComponentsInChildren<MonoBehaviour>(true))behaviour.enabled=false;
        foreach(var particles in go.GetComponentsInChildren<ParticleSystem>(true))particles.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        if(horizontal)go.transform.rotation=Quaternion.Euler(0,90,0);
        var bounds=BoundsOf(go);
        if(length>0){go.transform.localScale*=length/Mathf.Max(bounds.size.x,bounds.size.y,bounds.size.z);bounds=BoundsOf(go);}
        go.transform.position+=position-bounds.center;
        if(!horizontal)go.transform.position+=Vector3.up*(BoundsOf(go).extents.y);
    }
    private static Bounds BoundsOf(GameObject go)
    {
        var renderers=go.GetComponentsInChildren<Renderer>(true).Where(r=>!(r is ParticleSystemRenderer)).ToArray();
        if(renderers.Length==0)throw new InvalidOperationException("候选没有可渲染网格");
        var bounds=renderers[0].bounds;foreach(var renderer in renderers.Skip(1))bounds.Encapsulate(renderer.bounds);return bounds;
    }
}
