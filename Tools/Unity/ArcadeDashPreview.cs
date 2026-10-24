using System;
using System.IO;
using System.Reflection;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Playables;
using UnityEngine.Animations;
using Object=UnityEngine.Object;

/// <summary>隔离场景渲染正式冲刺表现的三个时间点，不进入对局或读写存档。</summary>
public static class ArcadeDashPreview
{
    public static string Run()
    {
        var scene=EditorSceneManager.NewPreviewScene();var old=RenderTexture.active;
        var rt=new RenderTexture(900,720,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
        var strip=new Texture2D(2700,720,TextureFormat.RGB24,false);
        Material floorMaterial=null;VolumeProfile profile=null;IDisposable effect=null;
        PlayableGraph pose=default;
        try
        {
            var root=new GameObject("DashPreview");SceneManager.MoveGameObjectToScene(root,scene);
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(floor,scene);
            floor.transform.position=new Vector3(0,-.2f,0);floor.transform.localScale=new Vector3(46,.3f,46);
            floorMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit"));floorMaterial.SetColor("_BaseColor",new Color(.22f,.25f,.28f));floor.GetComponent<Renderer>().sharedMaterial=floorMaterial;
            for(int x=-12;x<=12;x++)for(int z=-12;z<=12;z++)
            {
                var tile=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(tile,scene);
                tile.transform.position=new Vector3(x*1.5f,-.02f,z*1.5f);tile.transform.localScale=new Vector3(1.47f,.1f,1.47f);tile.GetComponent<Renderer>().sharedMaterial=floorMaterial;
            }
            var player=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"));SceneManager.MoveGameObjectToScene(player,scene);
            foreach(var script in player.GetComponentsInChildren<MonoBehaviour>(true))script.enabled=false;
            player.transform.position=Vector3.zero;player.transform.rotation=Quaternion.Euler(0,135,0);
            var run=AssetDatabase.LoadAllAssetsAtPath("Assets/Game/Art/Player/Animations/Mixamo/Imported/StandardRun.fbx").OfType<AnimationClip>().FirstOrDefault(c=>!c.name.StartsWith("__"));
            var animator=player.GetComponentInChildren<Animator>();
            if(run!=null&&animator!=null)
            {
                pose=PlayableGraph.Create("DashPreviewPose");pose.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var output=AnimationPlayableOutput.Create(pose,"Pose",animator);
                var clip=AnimationClipPlayable.Create(pose,run);output.SetSourcePlayable(clip);pose.Play();pose.Evaluate(.15f);
            }
            var lightGo=new GameObject("Light",typeof(Light));SceneManager.MoveGameObjectToScene(lightGo,scene);
            var light=lightGo.GetComponent<Light>();light.type=LightType.Directional;light.intensity=1.7f;light.transform.rotation=Quaternion.Euler(55,-35,0);
            var cameraGo=new GameObject("Camera",typeof(Camera));SceneManager.MoveGameObjectToScene(cameraGo,scene);
            var camera=cameraGo.GetComponent<Camera>();camera.scene=scene;camera.transform.rotation=Quaternion.Euler(72,45,0);camera.transform.position=Vector3.up-camera.transform.forward*11;
            camera.orthographic=false;camera.fieldOfView=45;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.03f,.07f,.08f);camera.allowHDR=true;
            // 预览明确使用轻量 Bloom，正式项目沿用自己的相机后处理设置。
            camera.GetUniversalAdditionalCameraData().renderPostProcessing=true;
            var volumeGo=new GameObject("PreviewBloom",typeof(Volume));SceneManager.MoveGameObjectToScene(volumeGo,scene);var volume=volumeGo.GetComponent<Volume>();
            profile=ScriptableObject.CreateInstance<VolumeProfile>();volume.sharedProfile=profile;volume.isGlobal=true;volume.priority=100;
            var bloom=profile.Add<Bloom>(true);bloom.threshold.Override(.9f);bloom.intensity.Override(.5f);
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Art/Arcade/VFX/Dash/Dash.prefab");
            var type=typeof(Game.ArcadeCombatViewController).Assembly.GetType("Game.ArcadeDashPresentation");
            effect=(IDisposable)Activator.CreateInstance(type,root.transform,prefab.transform);
            var play=type.GetMethod("Set");var tick=type.GetMethod("Tick");
            camera.targetTexture=rt;
            float[] times={.035f,.17f,.37f};
            for(int i=0;i<3;i++)
            {
                if(i>0){effect.Dispose();effect=(IDisposable)Activator.CreateInstance(type,root.transform,prefab.transform);}
                play.Invoke(effect,new object[]{0L,Vector2.zero,new Vector2(.707f,-.707f),.28f,true});tick.Invoke(effect,new object[]{times[i]});
                foreach(var ps in root.GetComponentsInChildren<ParticleSystem>(true))ps.Simulate(times[i],false,true,true);
                camera.Render();RenderTexture.active=rt;strip.ReadPixels(new Rect(0,0,900,720),i*900,0,false);
            }
            strip.Apply();string dir=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Backup/Artifacts/DashVfx-20261018"));Directory.CreateDirectory(dir);
            string file=Path.Combine(dir,"three-stages.png");File.WriteAllBytes(file,strip.EncodeToPNG());return file;
        }
        finally
        {
            if(pose.IsValid())pose.Destroy();
            effect?.Dispose();RenderTexture.active=old;EditorSceneManager.ClosePreviewScene(scene);
            Object.DestroyImmediate(rt);Object.DestroyImmediate(strip);if(floorMaterial!=null)Object.DestroyImmediate(floorMaterial);if(profile!=null)Object.DestroyImmediate(profile);
        }
    }
}
