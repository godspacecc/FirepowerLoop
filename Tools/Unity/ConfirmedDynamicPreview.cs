using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

/// <summary>待确认动态效果的独立临时场景；只输出帧图，不接入正式资源或对局。</summary>
public static class ConfirmedDynamicPreview
{
    public static string Drone()=>RunDrone(false);
    public static string DroneFormal()=>RunDrone(true);
    private static string RunDrone(bool formal)
    {
        if(EditorApplication.isPlaying||EditorApplication.isCompiling)throw new InvalidOperationException("仅编辑态临时预览");
        var prior=SceneManager.GetActiveScene();var oldRt=RenderTexture.active;
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        RenderTexture rt=null;Texture2D pixels=null;var materials=new List<Material>();
        try
        {
            SceneManager.SetActiveScene(scene);
            var player=Object.Instantiate(Load("Characters/GrayboxPlayer"));
            var presenter=player.GetComponent<CharacterWeaponPresenter>();
            if(!presenter.SetWeaponId("arcade.weapon.basic"))throw new InvalidOperationException(presenter.LastError);
            presenter.SetManualPoseSampling(true);presenter.BodyAnimator.Rebind();presenter.BodyAnimator.Update(0);
            presenter.Current.Animations.Idle.Clip.SampleAnimation(presenter.BodyAnimator.gameObject,.1f);presenter.ApplyOffhandGrip(presenter.Current.Animations.Idle);
            foreach(var c in player.GetComponentsInChildren<Canvas>(true))c.enabled=false;
            player.transform.rotation=Quaternion.Euler(0,90,0);
            var drone=Object.Instantiate(Load(formal?"SceneObjects/ArcadeTimedDroneVisual":"SceneObjects/ArcadeSupportDroneVisual"));if(!formal)Fit(drone,.85f);
            var enemy=Object.Instantiate(Load("Characters/ArcadeZombieShambler"));enemy.transform.position=new Vector3(2.6f,0,.5f);enemy.transform.rotation=Quaternion.Euler(0,-90,0);
            foreach(var c in enemy.GetComponentsInChildren<Canvas>(true))c.enabled=false;
            var bulletMat=Material(new Color(1,.85f,.35f),true,materials);
            var bullets=new GameObject[4];for(int i=0;i<bullets.Length;i++)bullets[i]=Primitive(PrimitiveType.Sphere,bulletMat,Vector3.one*.1f);
            var explosion=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(formal?"Assets/Game/Prefabs/SceneObjects/ArcadeDroneExplosion.prefab":"Assets/Game/Art/Arcade/VFX/FirstBatch/Prefabs/FireImpact.prefab"));explosion.SetActive(false);
            var floor=Primitive(PrimitiveType.Cube,Material(new Color(.16f,.22f,.23f),false,materials),new Vector3(18,.15f,12));floor.transform.position=Vector3.down*.12f;
            foreach(var root in scene.GetRootGameObjects())foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;
            var light=new GameObject("PreviewSun",typeof(Light)).GetComponent<Light>();light.type=LightType.Directional;light.intensity=1.8f;light.cullingMask=1<<31;light.transform.rotation=Quaternion.Euler(48,-30,0);
            var camera=new GameObject("PreviewCamera",typeof(Camera)).GetComponent<Camera>();camera.cullingMask=1<<31;camera.orthographic=true;camera.orthographicSize=3.25f;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.06f,.1f,.12f);camera.transform.position=new Vector3(0,6,-9);camera.transform.LookAt(new Vector3(0,.9f,0));
            rt=new RenderTexture(960,540,24);pixels=new Texture2D(960,540,TextureFormat.RGB24,false);camera.targetTexture=rt;
            string folder=Path.GetFullPath(formal?"Temp/ConfirmedDynamicPreview/drone-formal":"Temp/ConfirmedDynamicPreview/drone");Directory.CreateDirectory(folder);
            for(int frame=0;frame<120;frame++)
            {
                float time=frame/10f;int tier=time<2?1:time<4?2:time<6?3:1;
                var tint=tier==2?new Color(1,.2f,.15f):tier==3?new Color(.7f,.15f,1):Color.white;
                player.transform.position=new Vector3(-1.6f+Mathf.Sin(time*.8f)*.25f,0,-.4f);
                var follow=player.transform.position+new Vector3(.85f,1.35f,.35f);
                var impact=enemy.transform.position+Vector3.up*.8f;
                drone.transform.position=time>=7&&time<7.45f?Vector3.Lerp(follow,impact,(time-7)/.45f):follow+Vector3.up*(Mathf.Sin(time*5)*.055f);
                drone.SetActive(time<7.45f||time>=9&&time<10.6f);enemy.SetActive(time<8.5f);
                foreach(var r in drone.GetComponentsInChildren<Renderer>(true))
                {var block=new MaterialPropertyBlock();block.SetColor("_BaseColor",Color.Lerp(Color.white,tint,tier==1?0:.55f));r.SetPropertyBlock(block);}
                foreach(var r in presenter.Visual.GetComponentsInChildren<Renderer>(true))
                {var block=new MaterialPropertyBlock();block.SetColor("_BaseColor",Color.Lerp(Color.white,tint,tier==1?0:.55f));r.SetPropertyBlock(block);}
                bulletMat.color=tint==Color.white?new Color(1,.85f,.35f):tint;
                for(int i=0;i<bullets.Length;i++)
                {
                    bullets[i].SetActive(time<7);float progress=Mathf.Repeat(time*(1.2f*tier)+i*.25f,1);
                    bullets[i].transform.position=Vector3.Lerp(drone.transform.position,impact,progress);
                }
                bool blast=time>=7.45f&&time<8.5f;explosion.SetActive(blast);
                if(blast){explosion.transform.position=impact;explosion.transform.localScale=Vector3.one*1.2f;foreach(var ps in explosion.GetComponentsInChildren<ParticleSystem>(true)){ps.Simulate(time-7.45f,false,true,false);ps.Pause();}}
                camera.Render();RenderTexture.active=rt;pixels.ReadPixels(new Rect(0,0,960,540),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(folder,frame.ToString("D3")+".png"),pixels.EncodeToPNG());
            }
            camera.targetTexture=null;return folder;
        }
        finally
        {
            RenderTexture.active=oldRt;if(pixels!=null)Object.DestroyImmediate(pixels);if(rt!=null)Object.DestroyImmediate(rt);
            SceneManager.SetActiveScene(prior);EditorSceneManager.CloseScene(scene,true);foreach(var m in materials)if(m!=null)Object.DestroyImmediate(m);
        }
    }
    private static GameObject Load(string path)=>AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/"+path+".prefab")??throw new InvalidOperationException(path);
    private static Material Material(Color color,bool unlit,List<Material> owned)
    {var m=new Material(Shader.Find(unlit?"Universal Render Pipeline/Unlit":"Universal Render Pipeline/Lit"));m.color=color;owned.Add(m);return m;}
    private static GameObject Primitive(PrimitiveType type,Material material,Vector3 size)
    {var go=GameObject.CreatePrimitive(type);go.GetComponent<Renderer>().sharedMaterial=material;go.transform.localScale=size;Object.DestroyImmediate(go.GetComponent<Collider>());return go;}
    private static void Fit(GameObject go,float size)
    {var renderers=go.GetComponentsInChildren<Renderer>(true);var b=renderers[0].bounds;foreach(var r in renderers.Skip(1))b.Encapsulate(r.bounds);go.transform.localScale*=size/Mathf.Max(b.size.x,b.size.y,b.size.z);}
}
