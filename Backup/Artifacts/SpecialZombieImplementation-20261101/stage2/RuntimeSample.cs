using System;using System.IO;using System.Linq;using System.Reflection;using System.Collections;using System.Collections.Generic;using UnityEngine;using Game;using QFramework;
public class Script {
static string Dir="E:/GitHub/FirepowerLoop/Backup/Artifacts/SpecialZombieImplementation-20261101/stage2";
static BindingFlags F=BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Instance;
static object Get(object o,string n)=>o.GetType().GetField(n,F).GetValue(o);
static object Call(object o,string n,params object[] args)=>o.GetType().GetMethod(n,F).Invoke(o,args);
static void Layer(GameObject o){foreach(var t in o.GetComponentsInChildren<Transform>(true))t.gameObject.layer=30;}
public static object Main(){
 var a=Game.Game.Interface;var s=a.GetSystem<SurvivorSystem>();if(!Application.isPlaying||!a.GetModel<SurvivorModel>().Snapshot.Active)throw new Exception("Need normal active survivor map");
 typeof(SurvivorSystem).GetField("menuPaused",F).SetValue(s,true);var view=(ArcadeCombatViewController)a.GetUtility<IArcadeCombatViewPort>();var ep=a.GetUtility<IArcadeEnemyPresentationPort>();var pp=a.GetUtility<IArcadeProjectilePresentationPort>();var root=(Transform)Get(view,"mapRoot");var player=(Vector2)Get(s,"player");
 var report=new List<string>{"Source: normal Play Boot -> SurvivorSceneFlowSystem.StartAsync, then controlled reflection sampling of real SurvivorSystem -> ArcadeCombatViewController -> EnemyCtrl/ProjectileCtrl. Not an unmodified full-match recording.","Loader="+ResourceLoader.Current.GetType().FullName,"Map="+root.name,"Start="+File.ReadAllText(Dir+"/start.txt")};
 foreach(var name in new[]{"Exploder","Caster","Shield"}){string address="Prefabs/Characters/ArcadeZombie"+name;var p=ResourceLoader.LoadAsset<GameObject>(address);if(p==null)throw new Exception(address);report.Add("Real LoadAsset success: "+address+" -> "+p.name);}
 // Hide existing actors only in this temporary sampled scene window.
 var all=(IDictionary)Get(s,"enemies");foreach(int id in all.Keys.Cast<int>().ToArray())view.RemoveEnemy(id,false);all.Clear();Call(s,"ClearSpecialEnemies");typeof(SurvivorSystem).GetField("identity",F).SetValue(s,100000);
 var cam=new GameObject("SpecialVerificationCamera",typeof(Camera)).GetComponent<Camera>();cam.cullingMask=1<<30;cam.orthographic=true;cam.orthographicSize=2.1f;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.07f,.1f,.14f);
 var focus=root.TransformPoint(new Vector3(player.x,1,player.y));cam.transform.position=focus+new Vector3(0,2,-8);cam.transform.LookAt(focus);var light=new GameObject("SpecialVerificationLight",typeof(Light)).GetComponent<Light>();light.type=LightType.Directional;light.intensity=1.8f;light.transform.eulerAngles=new Vector3(50,-35,0);light.cullingMask=1<<30;
 var motor=Get(view,"playerMotor");var transform=(Transform)motor.GetType().GetProperty("Transform")?.GetValue(motor);if(transform==null){var pv=UnityEngine.Object.FindObjectOfType<PlayerView>();transform=pv.transform;}Layer(transform.gameObject);
 var rt=new RenderTexture(960,540,24);var tex=new Texture2D(960,540,TextureFormat.RGB24,false);cam.targetTexture=rt;var old=RenderTexture.active;
 try{
 for(int kind=0;kind<3;kind++){
 string kindName=new[]{"Exploder","Caster","Shield"}[kind],config="enemy.arcade.zombie."+kindName.ToLowerInvariant();int id=100100+kind;var type=typeof(SurvivorSystem).Assembly.GetType("Game.ArcadeEnemyRuntime");var e=Activator.CreateInstance(type);type.GetField("Id").SetValue(e,id);type.GetField("ConfigId").SetValue(e,config);type.GetField("Position").SetValue(e,player+new Vector2(kind==0?1.2f:3,0));type.GetField("Health").SetValue(e,150);type.GetField("MaxHealth").SetValue(e,150);type.GetField("Damage").SetValue(e,10);all.Add(id,e);var folder=Dir+"/sample-"+kind;Directory.CreateDirectory(folder);
 for(int i=0;i<40;i++){
 if(kind==2&&i==20){typeof(SurvivorSystem).GetField("player",F).SetValue(s,player+new Vector2(4,0));}
 Call(s,"TickSpecialEnemy",e,.1f);if((int)Get(e,"Health")>0){view.SetEnemy(id,config,(Vector2)Get(e,"Position"),(int)Get(e,"Health"),(int)Get(e,"MaxHealth"));Call(s,"ProjectSpecialEnemy",e);}Call(s,"AdvanceHostileProjectiles",.1f);Call(s,"FlushSpecialDeaths");
 if(ep.TryGetEnemyPresentationRoot(id,out var er)){Layer(er.gameObject);var ev=er.GetComponent<EnemyView>();ev.Animator.Update(.1f);ev.LateTickCombatFeedback();}
 foreach(var o in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects()){} // keep captured temporary camera local only
 var current=(IDictionary)Get(pp,"hostileFireballs");foreach(GameObject o in current.Values)Layer(o);var blasts=(IDictionary)Get(pp,"enemyBlasts");foreach(GameObject o in blasts.Values)Layer(o);
 cam.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,960,540),0,0);tex.Apply();File.WriteAllBytes(folder+"/"+i.ToString("D3")+".png",tex.EncodeToPNG());
 }
 report.Add(kindName+": sampled 40 frames via real system methods; active hostile="+((IDictionary)Get(pp,"hostileFireballs")).Count+" enemy blasts="+((IDictionary)Get(pp,"enemyBlasts")).Count);view.RemoveEnemy(id,false);all.Remove(id);Call(s,"ClearSpecialEnemies");typeof(SurvivorSystem).GetField("player",F).SetValue(s,player);
 }
 pp.SetSurvivorHostileProjectile(800001,focus,Vector3.right);pp.SetSurvivorEnemyBlast(800002,focus,1.5f,.2f,1);s.Stop();
 report.Add("Stop/End active hostile="+((IDictionary)Get(pp,"hostileFireballs")).Count+" pool="+((ICollection)Get(pp,"hostileFireballPool")).Count+" blasts="+((IDictionary)Get(pp,"enemyBlasts")).Count+" blastpool="+((ICollection)Get(pp,"enemyBlastPool")).Count);File.WriteAllLines(Dir+"/runtime.txt",report);return report;
 }finally{RenderTexture.active=old;cam.targetTexture=null;UnityEngine.Object.Destroy(cam.gameObject);UnityEngine.Object.Destroy(light.gameObject);UnityEngine.Object.Destroy(rt);UnityEngine.Object.Destroy(tex);}
}}
