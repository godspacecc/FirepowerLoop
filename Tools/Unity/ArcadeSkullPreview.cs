using System;
using System.IO;
using Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

/// <summary>隔离采样正式头骨和短弹载体的原色、红级、紫级，预览不启动对局。</summary>
public static class ArcadeSkullPreview
{
    public static string Run()
    {
        if(EditorApplication.isPlaying||Application.dataPath.Replace('\\','/')!="E:/GitHub/FirepowerLoop/Client/Assets")throw new InvalidOperationException("仅本工程编辑态预览");
        var prior=SceneManager.GetActiveScene();var oldTarget=RenderTexture.active;var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        var ambient=RenderSettings.ambientLight;var mode=RenderSettings.ambientMode;RenderTexture target=null;Texture2D image=null;
        try
        {
            SceneManager.SetActiveScene(scene);RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;RenderSettings.ambientLight=new Color(.55f,.55f,.55f);
            for(int tier=0;tier<3;tier++)
            {
                float x=(tier-1)*2;
                if(tier>0)
                {
                    var skull=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/ArcadeDropSkull"+(tier==1?"Red":"Purple")+".prefab"));
                    skull.transform.position=new Vector3(x,0,-.7f);skull.transform.rotation=Quaternion.Euler(0,180,0);
                }
                for(int j=0;j<4;j++)
                {
                    var shot=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Combat/GrayboxBullet.prefab"));
                    shot.GetComponent<ProjectileView>().SetArcadePresentation("ArcadeBulletRapid"+(tier==1?"#Red":tier==2?"#Purple":""),false);
                    shot.GetComponent<ProjectileView>().ApplyPose(new Vector3(x,.1f,.15f+j*.5f),Vector3.forward);
                }
            }
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.position=new Vector3(0,-.1f,0);floor.transform.localScale=new Vector3(30,.1f,30);
            floor.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Art/Arcade/Environment/Content/Slate.mat");
            foreach(var root in scene.GetRootGameObjects())foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=30;
            var light=new GameObject("PreviewKey",typeof(Light)).GetComponent<Light>();light.type=LightType.Directional;light.intensity=1.6f;light.transform.eulerAngles=new Vector3(50,-35,0);light.cullingMask=1<<30;
            var camera=new GameObject("SkullPreviewCamera",typeof(Camera)).GetComponent<Camera>();camera.cullingMask=1<<30;camera.orthographic=true;camera.orthographicSize=1.5f;
            var focus=new Vector3(0,.15f,.4f);camera.transform.position=focus+new Vector3(0,7,-5);camera.transform.LookAt(focus);camera.clearFlags=CameraClearFlags.SolidColor;
            target=new RenderTexture(1440,720,24);image=new Texture2D(1440,720,TextureFormat.RGB24,false);camera.targetTexture=target;camera.Render();RenderTexture.active=target;
            image.ReadPixels(new Rect(0,0,1440,720),0,0);image.Apply();camera.targetTexture=null;
            var folder=Path.GetFullPath("Temp/SkullPowerPreview");Directory.CreateDirectory(folder);var file=Path.Combine(folder,"normal-red-purple.png");File.WriteAllBytes(file,image.EncodeToPNG());return file;
        }
        finally
        {
            RenderTexture.active=oldTarget;RenderSettings.ambientMode=mode;RenderSettings.ambientLight=ambient;
            if(image!=null)Object.DestroyImmediate(image);if(target!=null)Object.DestroyImmediate(target);SceneManager.SetActiveScene(prior);EditorSceneManager.CloseScene(scene,true);
        }
    }
}
