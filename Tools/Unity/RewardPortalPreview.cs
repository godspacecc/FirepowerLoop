using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game;
using FirepowerLoop.Arcade;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

/// <summary>待确认的金币宝箱与整关传送门独立预览；仅输出帧图，绝不保存正式资源。</summary>
public static class RewardPortalPreview
{
    public static string Chest()=>Capture(false);
    public static string Portal()=>Capture(true);
    public static string ChestApplied()=>Capture(false,true);
    public static string PortalApplied()=>Capture(true,true);
    private static string Capture(bool portal,bool formal=false)
    {
        if(EditorApplication.isPlaying||EditorApplication.isCompiling)throw new InvalidOperationException("仅编辑态独立预览");
        var prior=SceneManager.GetActiveScene();var old=RenderTexture.active;
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        var owned=new List<Material>();RenderTexture rt=null;Texture2D pixels=null;Camera camera=null;
        try
        {
            SceneManager.SetActiveScene(scene);
            var player=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"));
            var presenter=player.GetComponent<CharacterWeaponPresenter>();presenter.SetWeaponId("arcade.weapon.basic");
            presenter.SetManualPoseSampling(true);presenter.BodyAnimator.Rebind();presenter.BodyAnimator.Update(0);
            presenter.Current.Animations.Idle.Clip.SampleAnimation(presenter.BodyAnimator.gameObject,.1f);presenter.ApplyOffhandGrip(presenter.Current.Animations.Idle);
            foreach(var c in player.GetComponentsInChildren<Canvas>(true))c.enabled=false;
            player.transform.position=new Vector3(-2.5f,0,0);player.transform.rotation=Quaternion.Euler(0,90,0);
            var floor=Primitive(PrimitiveType.Cube,Mat(new Color(.19f,.25f,.24f),owned),new Vector3(18,.15f,12));floor.transform.position=Vector3.down*.13f;
            var template=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab").transform.Find("Content");
            GameObject chest=null,door=null;var coins=new List<GameObject>();var targets=new List<Vector3>();var starts=new List<Vector3>();
            var debris=new List<GameObject>();var collected=new HashSet<int>();
            if(portal)
            {
                door=new GameObject("UnconfirmedStagePortal");door.transform.position=new Vector3(.5f,1.45f,.4f);
                var bright=Mat(new Color(.2f,1f,.75f),owned);var dark=Mat(new Color(.03f,.23f,.24f),owned);
                var ring=Circle("PortalOuter",1.3f,.12f,bright,door.transform);
                Circle("PortalInner",1.14f,.055f,bright,door.transform);
                var disc=Primitive(PrimitiveType.Cylinder,dark,new Vector3(2.26f,.012f,2.26f));disc.transform.SetParent(door.transform,false);disc.transform.localRotation=Quaternion.Euler(90,0,0);
                for(int j=0;j<5;j++)
                {
                    var swirl=new GameObject("Flow"+j,typeof(LineRenderer));swirl.transform.SetParent(door.transform,false);var line=swirl.GetComponent<LineRenderer>();
                    line.useWorldSpace=false;line.sharedMaterial=bright;line.widthMultiplier=.04f;line.positionCount=45;
                    for(int i=0;i<45;i++){float r=.1f+i/44f;float a=i/44f*4.8f+j*Mathf.PI*.4f;line.SetPosition(i,new Vector3(Mathf.Cos(a)*r,Mathf.Sin(a)*r,-.04f));}
                }
            }
            else
            {
                chest=Object.Instantiate(template.Find("TreasureEntrance/Chest").gameObject);Fit(chest,1.2f);chest.name="UnconfirmedRoundChest";
                var gold=Mat(new Color(1,.66f,.1f),owned);
                for(int i=0;i<24;i++)
                {
                    GameObject coin=i%4==0?Object.Instantiate(template.Find("GoldBar").gameObject):Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/ArcadeDropCoin.prefab"));
                    coin.name="Reward"+i;Fit(coin,i%4==0?.5f:.3f);coins.Add(coin);
                    float angle=i*2.399963f,radius=.75f+(i%5)*.32f;targets.Add(new Vector3(Mathf.Cos(angle)*radius,.13f,Mathf.Sin(angle)*radius));starts.Add(new Vector3(0,.55f,0));
                }
                for(int i=0;i<8;i++)debris.Add(Primitive(PrimitiveType.Cube,Mat(new Color(.28f,.15f,.06f),owned),new Vector3(.23f,.09f,.2f)));
            }
            GameObject formalRoot=null;ArcadeRoundRewardVisual formalChest=null;ArcadeStagePortalVisual formalPortal=null;ArcadeRoundRewardState rewardState=null;ArcadeRoundRewardTuning tuning=null;
            if(formal)
            {
                if(portal){Object.DestroyImmediate(door);formalRoot=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/ArcadeStagePortalVisual.prefab"));formalRoot.transform.position=new Vector3(.5f,0,.4f);formalPortal=formalRoot.GetComponent<ArcadeStagePortalVisual>();}
                else
                {
                    Object.DestroyImmediate(chest);foreach(var c in coins)Object.DestroyImmediate(c);foreach(var part in debris)Object.DestroyImmediate(part);
                    formalRoot=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/ArcadeRoundRewardVisual.prefab"));formalChest=formalRoot.GetComponent<ArcadeRoundRewardVisual>();
                    var row=new TbArcadeRoundReward(new Luban.ByteBuf(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Config/tbarcaderoundreward.bytes").bytes)).DataList[0];tuning=ArcadeRoundRewardTuning.FromConfig(row);ArcadeRoundRewardRules.TryStart(ref rewardState,tuning,1,0,0);
                }
            }
            foreach(var root in scene.GetRootGameObjects())foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;
            var sun=new GameObject("PreviewSun",typeof(Light)).GetComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.8f;sun.cullingMask=1<<31;sun.transform.rotation=Quaternion.Euler(48,-30,0);
            camera=new GameObject("PreviewCamera",typeof(Camera)).GetComponent<Camera>();camera.cullingMask=1<<31;camera.orthographic=true;camera.orthographicSize=3.25f;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.06f,.1f,.12f);camera.transform.position=new Vector3(0,6,-9);camera.transform.LookAt(new Vector3(0,1,0));
            rt=new RenderTexture(960,540,24);pixels=new Texture2D(960,540,TextureFormat.RGB24,false);camera.targetTexture=rt;
            string folder=Path.GetFullPath("Temp/ConfirmedDynamicPreview/"+(portal?"portal":"chest")+(formal?"-formal":""));Directory.CreateDirectory(folder);
            for(int frame=0;frame<90;frame++)
            {
                float time=frame/10f;
                if(formal)
                {
                    if(portal){formalPortal.Apply(Mathf.Max(0,time-1.5f),.75f);player.transform.position=new Vector3(Mathf.Lerp(-2.5f,.5f,Mathf.Clamp01((time-4.2f)/2.2f)),0,.4f);player.SetActive(time<6.5f);}
                    else
                    {
                        if(frame>0)ArcadeRoundRewardRules.Advance(rewardState,tuning,.1f);
                        player.transform.position=new Vector3(time<3.3f?-2.4f+Mathf.Sin(time*2)*.25f:Mathf.Lerp(-2.4f,2.8f,Mathf.Clamp01((time-3.5f)/3)),0,0);
                        if(time>=3.3f)foreach(var item in rewardState.Items)ArcadeRoundRewardRules.TryCollect(rewardState,tuning,item.Id,player.transform.position.x,player.transform.position.z,true,out _);
                        formalChest.Apply(rewardState,tuning);
                    }
                }
                else if(portal)
                {
                    float amount=Mathf.SmoothStep(0,1,Mathf.Clamp01((time-1.5f)/.75f));door.SetActive(amount>0);door.transform.localScale=Vector3.one*amount;
                    foreach(Transform child in door.transform)if(child.name.StartsWith("Flow"))child.localRotation=Quaternion.Euler(0,0,time*35);
                    player.transform.position=new Vector3(Mathf.Lerp(-2.5f,.5f,Mathf.Clamp01((time-4.2f)/2.2f)),0,.4f);player.SetActive(time<6.5f);
                }
                else
                {
                    chest.SetActive(time<1.5f);chest.transform.position=new Vector3(0,.25f+4*(1-Mathf.Clamp01(time/1.1f))*(1-Mathf.Clamp01(time/1.1f)),0);
                    player.transform.position=new Vector3(time<3.3f?-2.4f+Mathf.Sin(time*2)*.25f:Mathf.Lerp(-2.4f,2.8f,Mathf.Clamp01((time-3.5f)/3)),0,0);
                    float progress=Mathf.Clamp01((time-1.5f)/1.7f);
                    for(int i=0;i<coins.Count;i++)
                    {
                        var coin=coins[i];coin.transform.position=Vector3.Lerp(starts[i],targets[i],progress)+Vector3.up*(Mathf.Sin(progress*Mathf.PI)*(1+(i%4)*.13f));
                        coin.transform.rotation=Quaternion.Euler(i%4==0?0:90,progress*240+i*37,0);
                        if(time>=3.3f&&Vector3.Distance(player.transform.position,coin.transform.position)<.6f)collected.Add(i);
                        coin.SetActive(time>=1.5f&&!collected.Contains(i));
                    }
                    for(int i=0;i<debris.Count;i++)
                    {
                        var part=debris[i];part.SetActive(time>=1.5f&&time<2.7f);float a=i*Mathf.PI*.25f,spread=Mathf.Clamp01((time-1.5f)/1.2f)*2;
                        part.transform.position=new Vector3(Mathf.Cos(a)*spread,.4f+Mathf.Sin(spread*Mathf.PI*.5f),Mathf.Sin(a)*spread);part.transform.rotation=Quaternion.Euler(time*110,i*45,time*40);
                    }
                }
                camera.Render();RenderTexture.active=rt;pixels.ReadPixels(new Rect(0,0,960,540),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(folder,frame.ToString("D3")+".png"),pixels.EncodeToPNG());
            }
            return folder;
        }
        finally
        {
            if(camera!=null)camera.targetTexture=null;RenderTexture.active=old;if(pixels!=null)Object.DestroyImmediate(pixels);if(rt!=null)Object.DestroyImmediate(rt);
            SceneManager.SetActiveScene(prior);EditorSceneManager.CloseScene(scene,true);foreach(var m in owned)if(m!=null)Object.DestroyImmediate(m);
        }
    }
    private static Material Mat(Color color,List<Material> owned){var m=new Material(Shader.Find("Universal Render Pipeline/Unlit"));m.color=color;owned.Add(m);return m;}
    private static GameObject Primitive(PrimitiveType type,Material mat,Vector3 scale){var o=GameObject.CreatePrimitive(type);o.transform.localScale=scale;o.GetComponent<Renderer>().sharedMaterial=mat;Object.DestroyImmediate(o.GetComponent<Collider>());return o;}
    private static LineRenderer Circle(string name,float radius,float width,Material mat,Transform parent)
    {var o=new GameObject(name,typeof(LineRenderer));o.transform.SetParent(parent,false);var l=o.GetComponent<LineRenderer>();l.useWorldSpace=false;l.loop=true;l.sharedMaterial=mat;l.widthMultiplier=width;l.positionCount=80;for(int i=0;i<80;i++){float a=i*Mathf.PI*2/80;l.SetPosition(i,new Vector3(Mathf.Cos(a)*radius,Mathf.Sin(a)*radius,0));}return l;}
    private static void Fit(GameObject go,float size){var rs=go.GetComponentsInChildren<Renderer>(true);var b=rs[0].bounds;foreach(var r in rs.Skip(1))b.Encapsulate(r.bounds);go.transform.localScale*=size/Mathf.Max(b.size.x,b.size.y,b.size.z);}
}
