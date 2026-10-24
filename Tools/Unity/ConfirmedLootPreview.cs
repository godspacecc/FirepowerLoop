using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

/// <summary>已确认玩法的独立尺寸与持枪候选预览；仅修改临时实例，不保存任何正式资源。</summary>
public static class ConfirmedLootPreview
{
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    public static string Run()
        => Capture(false);
    public static string RunFormal()
        => Capture(true);
    private static string Capture(bool formal)
    {
        if(EditorApplication.isPlaying||EditorApplication.isCompiling)throw new InvalidOperationException("仅编辑态隔离预览");
        var prior=SceneManager.GetActiveScene();var oldRt=RenderTexture.active;
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        RenderTexture rt=null;Texture2D pixels=null;Material floorMat=null;
        try
        {
            SceneManager.SetActiveScene(scene);
            var player=Object.Instantiate(Load("Characters/GrayboxPlayer"));
            var presenter=player.GetComponent<CharacterWeaponPresenter>();
            var entry=JsonUtility.FromJson<CharacterWeaponCatalog.Entry>(JsonUtility.ToJson(presenter.Catalog.Find("arcade.weapon.rapid")));
            var source=new GameObject("TemporaryAKSource");source.transform.position=new Vector3(1000,0,0);
            var ak=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Art/Arcade/Weapons/ArcadeRifle.fbx"),source.transform);
            ak.transform.localPosition=new Vector3(0,-.03f,-.05f);ak.transform.localRotation=Quaternion.identity;ak.transform.localScale=Vector3.one*.865f;
            typeof(CharacterWeaponCatalog.Entry).GetField("visualSource",Private).SetValue(entry,source);
            if(formal)entry=presenter.Catalog.Find("arcade.weapon.basic");
            if(!presenter.SetWeapon(entry))throw new InvalidOperationException(presenter.LastError);
            presenter.SetManualPoseSampling(true);presenter.BodyAnimator.Rebind();presenter.BodyAnimator.Update(0);
            presenter.Current.Animations.Idle.Clip.SampleAnimation(presenter.BodyAnimator.gameObject,.15f);
            presenter.ApplyOffhandGrip(presenter.Current.Animations.Idle);
            player.transform.position=new Vector3(-2.4f,0,0);player.transform.rotation=Quaternion.Euler(0,145,0);
            foreach(var canvas in player.GetComponentsInChildren<Canvas>(true))canvas.enabled=false;

            string[] names={"ArcadeDropSkullRed","ArcadeDropSkullPurple","ArcadeSupportDroneVisual","ArcadeFlamethrower"};
            for(int i=0;i<names.Length;i++)
            {
                var drop=Object.Instantiate(Load("SceneObjects/TestResourcePickup"));
                drop.name="TemporaryDrop_"+names[i];drop.transform.position=new Vector3(-.6f+i*1.85f,.3f,0);
                var pickup=drop.GetComponent<ResourcePickupView>();pickup.SetArcadePresentation(Load("SceneObjects/"+names[i]),"Prefabs/SceneObjects/"+names[i]);
                pickup.RefreshDisplay("",1);pickup.SetNameVisible(false);if(i>=2)pickup.FitArcadeVisual(1.7f);
                if(i<2)drop.transform.rotation=Quaternion.Euler(0,180,0);
                if(i==3)
                {
                    pickup.SetWeaponDelivery(true,0,2);
                    // 预览候选尺寸：缩小箱体，武器重新放大抬高，正式脚本不变。
                    var delivery=drop.GetComponentInChildren<ArcadeWeaponDeliveryView>(true);
                    if(!formal&&delivery!=null)delivery.transform.localScale*=.6f;
                    var visual=(GameObject)typeof(ResourcePickupView).GetField("activeArcadeVisual",Private).GetValue(pickup);
                    visual.transform.localRotation=Quaternion.Euler(0,110,90);
                    pickup.FitArcadeVisual(1.7f);pickup.SetNameVisible(false);
                }
            }
            floorMat=new Material(Shader.Find("Universal Render Pipeline/Lit"));floorMat.color=new Color(.16f,.21f,.22f);
            var ground=GameObject.CreatePrimitive(PrimitiveType.Cube);ground.transform.position=new Vector3(1,-.12f,0);ground.transform.localScale=new Vector3(13,.2f,6);ground.GetComponent<Renderer>().sharedMaterial=floorMat;
            foreach(var root in scene.GetRootGameObjects())foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;
            var light=new GameObject("Key",typeof(Light)).GetComponent<Light>();light.type=LightType.Directional;light.intensity=1.8f;light.transform.rotation=Quaternion.Euler(45,-30,0);light.cullingMask=1<<31;
            var camera=new GameObject("PreviewCamera",typeof(Camera)).GetComponent<Camera>();camera.cullingMask=1<<31;camera.orthographic=true;camera.orthographicSize=3f;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.07f,.12f,.14f);
            rt=new RenderTexture(1800,900,24);pixels=new Texture2D(1800,900,TextureFormat.RGB24,false);camera.targetTexture=rt;
            string folder=Path.GetFullPath("../Backup/Artifacts/ConfirmedLoot-20261022");Directory.CreateDirectory(folder);
            camera.transform.position=new Vector3(1,5.4f,-9);camera.transform.LookAt(new Vector3(1,.8f,0));Capture(camera,rt,pixels,Path.Combine(folder,formal?"drop-size-applied.png":"drop-size-v2.png"));
            foreach(var pickup in scene.GetRootGameObjects().Where(g=>g.name.StartsWith("TemporaryDrop_")))pickup.SetActive(false);
            camera.orthographicSize=1.2f;camera.transform.position=player.transform.position+new Vector3(2,1.8f,-3);camera.transform.LookAt(player.transform.position+Vector3.up*.95f);Capture(camera,rt,pixels,Path.Combine(folder,formal?"ak-held-applied.png":"ak-held-candidate.png"));
            camera.targetTexture=null;return folder;
        }
        finally
        {
            RenderTexture.active=oldRt;if(pixels!=null)Object.DestroyImmediate(pixels);if(rt!=null)Object.DestroyImmediate(rt);if(floorMat!=null)Object.DestroyImmediate(floorMat);
            SceneManager.SetActiveScene(prior);EditorSceneManager.CloseScene(scene,true);
        }
    }
    private static GameObject Load(string path)=>AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/"+path+".prefab")??throw new InvalidOperationException(path);
    private static void Capture(Camera camera,RenderTexture rt,Texture2D pixels,string file)
    {camera.Render();RenderTexture.active=rt;pixels.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);pixels.Apply();File.WriteAllBytes(file,pixels.EncodeToPNG());}
}
