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

/// <summary>已确认火箭正式弹体和短尾隔离实拍；轨迹手动采样，伤害另由定向检查验证。</summary>
public static class PurpleRocketAppliedPreview
{
    static Transform root;static Camera camera;static readonly List<Material> materials=new List<Material>();
    static Material purple;static int Layer=30;static string folder;
    public static string Run()
    {
        if(EditorApplication.isPlaying||EditorApplication.isCompiling)throw new InvalidOperationException("仅编辑态临时预览");
        var prior=SceneManager.GetActiveScene();var previous=RenderTexture.active;var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);folder=Path.GetFullPath("Temp/PurpleRocketApplied-20261022");Directory.CreateDirectory(folder);
            root=new GameObject("TEMP_PURPLE_WEAPON_PROPOSAL").transform;purple=Mat(new Color(.8f,.3f,1));
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.SetParent(root);floor.transform.position=Vector3.down*.15f;floor.transform.localScale=new Vector3(25,.2f,19);floor.GetComponent<Renderer>().sharedMaterial=Mat(new Color(.14f,.22f,.24f));
            var hero=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"),root);
            hero.transform.position=new Vector3(-5,0,-1.5f);hero.transform.rotation=Quaternion.Euler(0,90,0);
            var presenter=hero.GetComponent<CharacterWeaponPresenter>();presenter.SetManualPoseSampling(true);
            var targets=new List<GameObject>();
            foreach(var pos in new[]{new Vector3(5,0,0),new Vector3(6,0,2.6f),new Vector3(7,0,-2.3f)})
            {
                var target=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/ArcadeZombieShambler.prefab"),root);target.transform.position=pos;target.transform.rotation=Quaternion.Euler(0,260,0);
                var a=target.GetComponentInChildren<Animator>(true);a.Rebind();a.Update(0);AssetDatabase.LoadAllAssetsAtPath("Assets/Game/Art/Arcade/Enemies/Humanoid/Animations/ArcadeEnemyIdle.fbx").OfType<AnimationClip>().First(c=>!c.name.StartsWith("__")).SampleAnimation(a.gameObject,.1f);a.enabled=false;targets.Add(target);
            }
            foreach(var c in root.GetComponentsInChildren<Canvas>(true))c.enabled=false;
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.transform.SetParent(root);wall.name="RayReflectWall";wall.transform.position=new Vector3(0,.5f,4);wall.transform.localScale=new Vector3(6,1,.35f);wall.GetComponent<Renderer>().sharedMaterial=Mat(new Color(.33f,.40f,.42f));
            var reflectWall=GameObject.CreatePrimitive(PrimitiveType.Cube);reflectWall.transform.SetParent(root);reflectWall.transform.position=new Vector3(1,.5f,-4.5f);reflectWall.transform.localScale=new Vector3(7,1,.35f);reflectWall.GetComponent<Renderer>().sharedMaterial=wall.GetComponent<Renderer>().sharedMaterial;
            var light=new GameObject("PreviewLight",typeof(Light)).GetComponent<Light>();light.type=LightType.Directional;light.intensity=1.2f;light.cullingMask=1<<Layer;light.transform.rotation=Quaternion.Euler(50,-35,0);
            camera=new GameObject("PreviewCamera",typeof(Camera)).GetComponent<Camera>();camera.cullingMask=1<<Layer;camera.orthographic=true;camera.orthographicSize=5.4f;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.05f,.08f,.11f);camera.transform.position=new Vector3(1,15,-10);camera.transform.LookAt(new Vector3(1,0,0));
            for(int kind=0;kind<1;kind++)
            {
                string id=kind==0?"rocket":kind==1?"raygun":"grenade";presenter.SetWeaponId("arcade.weapon."+id);presenter.BodyAnimator.Rebind();presenter.BodyAnimator.Update(0);presenter.Current.Animations.Idle.Clip.SampleAnimation(presenter.BodyAnimator.gameObject,.1f);presenter.ApplyOffhandGrip(presenter.Current.Animations.Idle);
                wall.SetActive(kind==1);reflectWall.SetActive(kind==1);
                var display=new GameObject("TemporaryTrajectory").transform;display.SetParent(root);
                var shots=new List<ProjectileView>();int count=kind==0?6:kind==1?1:12;
                for(int j=0;j<count;j++)
                {
                    var shot=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Combat/"+(kind==2?"GrayboxGrenade":"GrayboxArrow")+".prefab"),display);var pv=shot.GetComponent<ProjectileView>();pv.SetArcadePresentation((kind==0?"ArcadePanzerschreckRocket":kind==1?"ArcadeRaygunBolt":"ArcadeGrenade")+"#Purple",false);shots.Add(pv);
                }
                var arcs=new List<LineRenderer>();for(int j=0;j<2;j++){var o=new GameObject("TemporaryElectricArc",typeof(LineRenderer));o.transform.SetParent(display);var line=o.GetComponent<LineRenderer>();line.sharedMaterial=purple;line.positionCount=13;line.widthMultiplier=.055f;arcs.Add(line);}
                var blastTemplate=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab").transform.Find("Content/ProjectileBlast");
                var blasts=new List<GameObject>();for(int j=0;j<12;j++){var b=Object.Instantiate(blastTemplate.gameObject,display);blasts.Add(b);}
                foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=Layer;
                for(int frame=0;frame<75;frame++)
                {
                    float time=frame/15f;float cycle=time%2.4f;targets[0].transform.position=new Vector3(5,0,Mathf.Sin(time*2)*.75f);
                    foreach(var arc in arcs)arc.gameObject.SetActive(false);foreach(var b in blasts)b.SetActive(false);
                    for(int j=0;j<shots.Count;j++)
                    {
                        float born=kind==0?(j/3)*1.1f+.25f:kind==1?.2f:.2f+j*.16f;
                        float age=(kind==0?cycle:time%2.8f)-born;float life=kind==1?1.2f:.8f;bool visible=age>=0&&age<life;shots[j].gameObject.SetActive(visible);
                        if(visible)
                        {
                            Vector3 start=new Vector3(-4,1,-1.5f),end=targets[kind==0?j%3:0].transform.position+Vector3.up*.8f;
                            float p=age/life;Vector3 at,dir;
                            if(kind==1)
                            {
                                var path=new[]{start,new Vector3(-1,1,3.7f),new Vector3(2,1,-4.25f),end};float step=p*3;int seg=Mathf.Min((int)step,2);at=Vector3.Lerp(path[seg],path[seg+1],step-seg);dir=path[seg+1]-path[seg];
                            }
                            else
                            {
                                at=Vector3.Lerp(start,end,p);at.z+=Mathf.Sin(p*Mathf.PI)*(kind==0?(j%3-1)*1.7f:0);at.y+=Mathf.Sin(p*Mathf.PI)*(kind==2?2.6f:.3f);
                                dir=end-at;if(dir.sqrMagnitude<.0001f)dir=Vector3.right;
                            }
                            shots[j].ApplyPose(at,dir.normalized);
                            var tail=shots[j].GetComponent<LineRenderer>();if(tail!=null){tail.SetPosition(0,at-dir.normalized*(kind==0?.8f:.45f));tail.SetPosition(1,at);}
                            foreach(var ps in shots[j].GetComponentsInChildren<ParticleSystem>()) {ps.Simulate(age,false,true,false);ps.Pause(false);}
                        }
                        float blastAge=age-life;
                        if(blastAge>=0&&blastAge<.42f)
                        {
                            var b=blasts[j];b.SetActive(true);b.transform.position=targets[kind==0?j%3:0].transform.position;b.transform.localScale=Vector3.one*((kind==2?3.2f:kind==1?1.35f:2.8f)/2.8f);
                            foreach(var ps in b.GetComponentsInChildren<ParticleSystem>(true))
                            {
                                if(kind==1&&(ps.name=="Burst"||ps.name=="Smoke")){ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);continue;}
                                if(kind==1){var main=ps.main;main.startColor=new ParticleSystem.MinMaxGradient(new Color(.8f,.3f,1));}
                                ps.Simulate(blastAge,false,true,false);ps.Pause(false);
                            }
                        }
                    }
                    if(kind==1&&(time%2.8f)>1.4f&&(time%2.8f)<1.85f)
                    {
                        for(int j=0;j<2;j++)
                        {
                            var line=arcs[j];line.gameObject.SetActive(true);var a=targets[0].transform.position+Vector3.up*.9f;var b=targets[j+1].transform.position+Vector3.up*.9f;
                            for(int k=0;k<13;k++){var p=Vector3.Lerp(a,b,k/12f);if(k>0&&k<12)p.x+=Mathf.Sin(k*7+frame*.9f)*.19f;line.SetPosition(k,p);}
                        }
                    }
                    Capture(kind.ToString()+"-"+frame.ToString("D3")+".png");
                }
                Object.DestroyImmediate(display.gameObject);
            }
            return folder;
        }
        finally
        {
            if(camera!=null)camera.targetTexture=null;RenderTexture.active=previous;SceneManager.SetActiveScene(prior);EditorSceneManager.CloseScene(scene,true);
            foreach(var m in materials)if(m!=null)Object.DestroyImmediate(m);materials.Clear();
        }
    }
    static Material Mat(Color color){var m=new Material(Shader.Find("Universal Render Pipeline/Unlit")){color=color};materials.Add(m);return m;}
    static void Capture(string name)
    {
        var target=RenderTexture.GetTemporary(960,600,24);var previous=RenderTexture.active;var image=new Texture2D(960,600,TextureFormat.RGB24,false);
        try{camera.targetTexture=target;camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,960,600),0,0);image.Apply();File.WriteAllBytes(Path.Combine(folder,name),image.EncodeToPNG());}
        finally{camera.targetTexture=null;RenderTexture.active=previous;Object.DestroyImmediate(image);RenderTexture.ReleaseTemporary(target);}
    }
}
