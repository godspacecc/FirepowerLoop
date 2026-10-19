using System;
using System.IO;
using Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

/// <summary>隔离采样正式拾取模板的下落、落地和开箱三阶段，不启动对局或保存场景。</summary>
public static class ArcadeWeaponDeliveryPreview
{
    public static string Run()
    {
        if(EditorApplication.isPlaying||Application.dataPath.Replace('\\','/')!="E:/GitHub/FirepowerLoop/Client/Assets")throw new InvalidOperationException("仅本工程编辑态预览");
        var prior=SceneManager.GetActiveScene();var oldTarget=RenderTexture.active;var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        var ambient=RenderSettings.ambientLight;var mode=RenderSettings.ambientMode;RenderTexture target=null;Texture2D image=null;
        try
        {
            SceneManager.SetActiveScene(scene);RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;RenderSettings.ambientLight=new Color(.55f,.55f,.55f);
            for(int stage=0;stage<3;stage++)
            {
                var instance=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/TestResourcePickup.prefab"));
                instance.transform.position=new Vector3((stage-1)*2.6f,.3f,0);instance.transform.rotation=Quaternion.Euler(0,20,0);
                var pickup=instance.GetComponent<ResourcePickupView>();pickup.SetNameVisible(false);
                pickup.SetArcadePresentation(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/ArcadeFlamethrower.prefab"),"Prefabs/SceneObjects/ArcadeFlamethrower");
                pickup.SetWeaponDelivery(true,1.2f,1.2f);pickup.SetWeaponDelivery(true,stage==0?.4f:stage==1?.3f:0,1.2f);
                foreach(var ps in instance.GetComponentsInChildren<ParticleSystem>(true)){if(ps.gameObject.activeInHierarchy){ps.Simulate(.12f,false,false,false);ps.Pause();}}
            }
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.position=new Vector3(0,-.1f,0);floor.transform.localScale=new Vector3(30,.1f,30);
            floor.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Art/Arcade/Environment/Content/Slate.mat");
            foreach(var root in scene.GetRootGameObjects())foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=30;
            var light=new GameObject("PreviewKey",typeof(Light)).GetComponent<Light>();light.type=LightType.Directional;light.intensity=1.6f;light.transform.eulerAngles=new Vector3(50,-35,0);light.cullingMask=1<<30;
            var camera=new GameObject("DeliveryPreviewCamera",typeof(Camera)).GetComponent<Camera>();camera.cullingMask=1<<30;camera.orthographic=true;camera.orthographicSize=2.8f;
            var focus=new Vector3(0,.9f,0);camera.transform.position=focus+new Vector3(0,7,-8);camera.transform.LookAt(focus);camera.clearFlags=CameraClearFlags.SolidColor;
            target=new RenderTexture(1440,800,24);image=new Texture2D(1440,800,TextureFormat.RGB24,false);camera.targetTexture=target;camera.Render();RenderTexture.active=target;
            image.ReadPixels(new Rect(0,0,1440,800),0,0);image.Apply();camera.targetTexture=null;
            var folder=Path.GetFullPath("Temp/WeaponDeliveryPreview");Directory.CreateDirectory(folder);var file=Path.Combine(folder,"fall-land-open.png");File.WriteAllBytes(file,image.EncodeToPNG());return file;
        }
        finally
        {
            RenderTexture.active=oldTarget;RenderSettings.ambientMode=mode;RenderSettings.ambientLight=ambient;
            if(image!=null)Object.DestroyImmediate(image);if(target!=null)Object.DestroyImmediate(target);SceneManager.SetActiveScene(prior);EditorSceneManager.CloseScene(scene,true);
        }
    }
}
