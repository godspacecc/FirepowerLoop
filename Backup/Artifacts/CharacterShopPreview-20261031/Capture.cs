using System;
using System.IO;
using Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
public static class CharacterShopPreview
{
    // 在隔离临时场景使用相同相机、姿势与原始尺寸，不保存任何正式资源。
    public static string Run()
    {
        if(Application.isPlaying||Application.dataPath.Replace('\\','/')!="E:/GitHub/FirepowerLoop/Client/Assets")throw new Exception("项目或状态不符");
        var prior=SceneManager.GetActiveScene();var active=RenderTexture.active;
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        RenderTexture target=null;Texture2D image=null;
        var folder="E:/GitHub/FirepowerLoop/Backup/Artifacts/CharacterShopPreview-20261031";
        try {
            SceneManager.SetActiveScene(scene);
            RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;RenderSettings.ambientLight=new Color(.62f,.65f,.70f);
            var cam=new GameObject("Camera",typeof(Camera)).GetComponent<Camera>();cam.cullingMask=1<<30;cam.orthographic=true;cam.orthographicSize=1.25f;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.08f,.11f,.15f);
            var focus=new Vector3(0,.96f,0);cam.transform.position=focus+new Vector3(0,.5f,-6);cam.transform.LookAt(focus);
            var light=new GameObject("Key",typeof(Light)).GetComponent<Light>();light.type=LightType.Directional;light.intensity=1.6f;light.transform.eulerAngles=new Vector3(45,-30,0);light.cullingMask=1<<30;
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.position=new Vector3(0,-.08f,0);floor.transform.localScale=new Vector3(20,.1f,20);floor.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Art/Arcade/Environment/Content/Slate.mat");
            target=new RenderTexture(400,600,24);image=new Texture2D(400,600,TextureFormat.RGB24,false);cam.targetTexture=target;
            for(int i=0;i<10;i++){
                var player=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"));
                try {
                    player.transform.position=Vector3.zero;player.transform.rotation=Quaternion.Euler(0,160,0);
                    var appearance=player.GetComponent<ClothingAppearanceView>();if(i>0&&!appearance.ApplyWholeBody(i))throw new Exception("造型绑定失败 "+i);
                    var presenter=player.GetComponent<CharacterWeaponPresenter>();if(!presenter.SetWeaponId("arcade.weapon.basic"))throw new Exception(presenter.LastError);
                    presenter.SetManualPoseSampling(true);presenter.BodyAnimator.Rebind();presenter.BodyAnimator.Update(0);
                    presenter.Current.Animations.Idle.Clip.SampleAnimation(presenter.BodyAnimator.gameObject,.2f);presenter.ApplyOffhandGrip(presenter.Current.Animations.Idle);
                    foreach(var a in player.GetComponentsInChildren<Animator>())a.enabled=false;
                    foreach(var c in player.GetComponentsInChildren<Canvas>(true))c.gameObject.SetActive(false);
                    foreach(var r in scene.GetRootGameObjects())foreach(var t in r.GetComponentsInChildren<Transform>(true))t.gameObject.layer=30;
                    cam.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,400,600),0,0);image.Apply();File.WriteAllBytes(folder+"/character-"+i+".png",image.EncodeToPNG());
                }finally{Object.DestroyImmediate(player);}
            }
            cam.targetTexture=null;return "10 portraits captured; scale unchanged; temporary scene only";
        }finally{
            RenderTexture.active=active;if(image!=null)Object.DestroyImmediate(image);if(target!=null){target.Release();Object.DestroyImmediate(target);}
            SceneManager.SetActiveScene(prior);EditorSceneManager.CloseScene(scene,true);
        }
    }
}
