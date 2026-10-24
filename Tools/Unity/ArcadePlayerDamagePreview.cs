using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

/// <summary>隔离场景采样正式玩家及受击倒地资源，不进入对局、不读写存档。</summary>
public static class ArcadePlayerDamagePreview
{
    public static string Run()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("仅在编辑态预览");
        var scene=EditorSceneManager.NewPreviewScene();var previous=RenderTexture.active;
        var rt=new RenderTexture(900,720,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
        var strip=new Texture2D(2700,720,TextureFormat.RGB24,false);
        Material floorMaterial=null;VolumeProfile profile=null;
        try
        {
            floorMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit"));floorMaterial.SetColor("_BaseColor",new Color(.19f,.23f,.25f));
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(floor,scene);
            floor.transform.position=Vector3.down*.2f;floor.transform.localScale=new Vector3(25,.3f,25);floor.GetComponent<Renderer>().sharedMaterial=floorMaterial;
            var lightGo=new GameObject("PreviewLight",typeof(Light));SceneManager.MoveGameObjectToScene(lightGo,scene);
            var light=lightGo.GetComponent<Light>();light.type=LightType.Directional;light.intensity=1.5f;light.transform.rotation=Quaternion.Euler(55,-35,0);
            var cameraGo=new GameObject("PreviewCamera",typeof(Camera));SceneManager.MoveGameObjectToScene(cameraGo,scene);
            var camera=cameraGo.GetComponent<Camera>();camera.scene=scene;camera.transform.rotation=Quaternion.Euler(58,30,0);camera.transform.position=Vector3.up*.6f-camera.transform.forward*7;
            camera.fieldOfView=45;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.03f,.07f,.08f);camera.allowHDR=true;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing=true;camera.targetTexture=rt;
            var volumeGo=new GameObject("PreviewBloom",typeof(Volume));SceneManager.MoveGameObjectToScene(volumeGo,scene);
            profile=ScriptableObject.CreateInstance<VolumeProfile>();var volume=volumeGo.GetComponent<Volume>();volume.sharedProfile=profile;volume.isGlobal=true;volume.priority=100;
            var bloom=profile.Add<Bloom>(true);bloom.threshold.Override(.9f);bloom.intensity.Override(.35f);
            for(int stage=0;stage<3;stage++)
            {
                var player=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"));SceneManager.MoveGameObjectToScene(player,scene);
                try
                {
                    foreach(var script in player.GetComponentsInChildren<MonoBehaviour>(true))script.enabled=false;
                    var view=player.GetComponent<Game.PlayerView>();view.SetArcadePresentationActive(true);
                    player.transform.rotation=Quaternion.Euler(0,165,0);
                    var animator=view.Animator;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;animator.Rebind();animator.Update(.01f);
                    if(stage==0){animator.Play("Idle",0,0);animator.Update(.1f);view.PlayHurtFeedback();view.TickCombatFeedback(.035f);}
                    else
                    {
                        view.SetAlivePresentation(false);float elapsed=stage==1?.42f:2f;
                        // 使用正式控制器死亡状态，逐帧求值保证 Humanoid 重定向及过渡完成。
                        for(float t=0;t<elapsed;t+=.02f)animator.Update(.02f);
                        view.TickCombatFeedback(elapsed);
                        if(stage==2){view.enabled=true;view.SetArcadeRescueActive(true);view.enabled=false;}
                    }
                    camera.Render();RenderTexture.active=rt;strip.ReadPixels(new Rect(0,0,900,720),stage*900,0,false);
                }
                finally{Object.DestroyImmediate(player);}
            }
            strip.Apply();string directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Backup/Artifacts/PlayerDamage-20261018"));Directory.CreateDirectory(directory);
            string path=Path.Combine(directory,"three-stages.png");File.WriteAllBytes(path,strip.EncodeToPNG());return path;
        }
        finally
        {
            RenderTexture.active=previous;EditorSceneManager.ClosePreviewScene(scene);
            Object.DestroyImmediate(rt);Object.DestroyImmediate(strip);if(floorMaterial!=null)Object.DestroyImmediate(floorMaterial);if(profile!=null)Object.DestroyImmediate(profile);
        }
    }
}
