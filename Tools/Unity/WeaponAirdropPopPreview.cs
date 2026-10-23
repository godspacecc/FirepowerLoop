using System;
using System.IO;
using Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

/// <summary>武器空投弹出候选：只操作隔离场景内的临时实例，不修改正式资源。</summary>
public static class WeaponAirdropPopPreview
{
    public static string Run()=>Capture(false);
    /// <summary>右侧直接使用正式表现入口，左侧仅临时还原旧动画作对照。</summary>
    public static string RunApplied()=>Capture(true);
    private static string Capture(bool applied)
    {
        if(EditorApplication.isPlaying||EditorApplication.isCompiling||Application.dataPath.Replace('\\','/')!="E:/GitHub/FirepowerLoop/Client/Assets")throw new InvalidOperationException("仅本工程编辑态预览");
        var prior=SceneManager.GetActiveScene();var old=RenderTexture.active;
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        RenderTexture target=null;Texture2D pixels=null;Camera camera=null;
        try
        {
            SceneManager.SetActiveScene(scene);
            var pickups=new ResourcePickupView[2];var crates=new ArcadeWeaponDeliveryView[2];var weapons=new Transform[2];var rests=new Vector3[2];
            for(int i=0;i<2;i++)
            {
                var go=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/TestResourcePickup.prefab"));
                go.transform.position=new Vector3(i==0?-2.65f:2.65f,.3f,0);
                pickups[i]=go.GetComponent<ResourcePickupView>();
                pickups[i].SetArcadePresentation(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/ArcadeFlamethrower.prefab"),"Prefabs/SceneObjects/ArcadeFlamethrower");
                pickups[i].SetWeaponDelivery(true,0,1.2f);
                crates[i]=go.GetComponentInChildren<ArcadeWeaponDeliveryView>(true);
                weapons[i]=go.transform.Find("ArcadeDropVisual");rests[i]=weapons[i].localPosition;
                pickups[i].SetWeaponDelivery(false,0,1.2f);
            }
            var player=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"));
            var presenter=player.GetComponent<CharacterWeaponPresenter>();presenter.SetWeaponId("arcade.weapon.basic");presenter.SetManualPoseSampling(true);
            presenter.BodyAnimator.Rebind();presenter.BodyAnimator.Update(0);presenter.Current.Animations.Idle.Clip.SampleAnimation(presenter.BodyAnimator.gameObject,.1f);presenter.ApplyOffhandGrip(presenter.Current.Animations.Idle);
            foreach(var canvas in player.GetComponentsInChildren<Canvas>(true))canvas.enabled=false;
            player.transform.position=new Vector3(0,0,.35f);player.transform.rotation=Quaternion.Euler(0,180,0);
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.position=Vector3.down*.1f;floor.transform.localScale=new Vector3(30,.1f,30);
            floor.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Art/Arcade/Environment/Content/Slate.mat");
            foreach(var root in scene.GetRootGameObjects())foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=30;
            var light=new GameObject("PreviewSun",typeof(Light)).GetComponent<Light>();light.type=LightType.Directional;light.intensity=1.6f;light.cullingMask=1<<30;light.transform.rotation=Quaternion.Euler(50,-35,0);
            camera=new GameObject("PreviewCamera",typeof(Camera)).GetComponent<Camera>();camera.cullingMask=1<<30;camera.orthographic=true;camera.orthographicSize=3.65f;
            var focus=new Vector3(0,1.5f,0);camera.transform.position=focus+new Vector3(0,8,-10);camera.transform.LookAt(focus);camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.055f,.08f,.11f);
            target=new RenderTexture(1120,630,24);pixels=new Texture2D(1120,630,TextureFormat.RGB24,false);camera.targetTexture=target;
            var folder=Path.GetFullPath(applied?"Temp/WeaponAirdropPopApplied":"Temp/WeaponAirdropPopPreview");Directory.CreateDirectory(folder);
            for(int frame=0;frame<144;frame++)
            {
                float time=frame/24f;bool slow=time>=3;float elapsed=Mathf.Max(0,(time%3-.35f)*(slow?.5f:1));
                if(frame==0||frame==72)foreach(var pickup in pickups)pickup.SetWeaponDelivery(false,0,1.2f);
                for(int i=0;i<2;i++)
                {
                    pickups[i].transform.rotation=Quaternion.Euler(0,elapsed*90,0);
                    pickups[i].SetWeaponDelivery(true,Mathf.Max(0,1.2f-elapsed),1.2f);pickups[i].SetNameVisible(false);
                    if(i==0)
                    {
                        foreach(var r in crates[i].GetComponentsInChildren<MeshRenderer>(true))r.enabled=true;
                        weapons[i].gameObject.SetActive(elapsed>=1.08f);weapons[i].localPosition=rests[i];
                    }
                    if(!applied&&i==1)
                    {
                        bool popped=elapsed>=.9f;
                        foreach(var r in crates[i].GetComponentsInChildren<MeshRenderer>(true))r.enabled=!popped;
                        weapons[i].gameObject.SetActive(popped);
                        float pop=Mathf.Clamp01((elapsed-.9f)/.3f);
                        float lift=Mathf.Lerp(-.35f,0,Mathf.SmoothStep(0,1,pop))+Mathf.Sin(pop*Mathf.PI)*.85f;
                        weapons[i].localPosition=rests[i]+Vector3.up*(lift/Mathf.Abs(pickups[i].transform.lossyScale.y));
                    }
                    foreach(var ps in pickups[i].GetComponentsInChildren<ParticleSystem>(true))if(ps.gameObject.activeInHierarchy){ps.Simulate(.05f,false,false,false);ps.Pause();}
                }
                camera.Render();RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1120,630),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(folder,frame.ToString("D3")+".png"),pixels.EncodeToPNG());
            }
            return folder;
        }
        finally
        {
            if(camera!=null)camera.targetTexture=null;RenderTexture.active=old;
            if(pixels!=null)Object.DestroyImmediate(pixels);if(target!=null)Object.DestroyImmediate(target);
            SceneManager.SetActiveScene(prior);EditorSceneManager.CloseScene(scene,true);
        }
    }
}
