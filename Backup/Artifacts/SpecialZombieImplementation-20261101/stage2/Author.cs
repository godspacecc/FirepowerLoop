using System;using System.IO;using System.Linq;using System.Collections.Generic;using Game;using UnityEngine;using UnityEditor;using Object=UnityEngine.Object;
public class Script {
static string Dir="Assets/Game/Art/Arcade/SpecialZombies";
static Material Mat(string name,Color c){var p=Dir+"/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(p);if(m==null){m=new Material(Shader.Find("Universal Render Pipeline/Lit"));m.color=c;AssetDatabase.CreateAsset(m,p);}return m;}
static GameObject Prop(PrimitiveType type,Transform bone,Vector3 world,Vector3 scale,Material mat){var o=GameObject.CreatePrimitive(type);o.name=mat.name;o.transform.position=world;o.transform.localScale=scale;o.transform.SetParent(bone,true);Object.DestroyImmediate(o.GetComponent<Collider>());o.GetComponent<Renderer>().sharedMaterial=mat;return o;}
public static object Main(){
 if(Application.isPlaying)throw new Exception("Edit only");Directory.CreateDirectory(Dir);AssetDatabase.Refresh();var results=new List<object>();
 var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/ArcadeZombieShambler.prefab");
 for(int kind=0;kind<3;kind++){
 var e=Object.Instantiate(source);e.transform.position=Vector3.zero;e.transform.rotation=Quaternion.identity;var v=e.GetComponent<EnemyView>();v.SetNavigationEnabled(false);v.BeginPresentationUse();var a=v.Animator;a.Rebind();a.Update(0);
 var bones=e.GetComponentsInChildren<Transform>(true);var chest=bones.First(t=>t.name=="Torso");var hand=bones.First(t=>t.name=="Middle1.L");var right=bones.First(t=>t.name=="Middle1.R");
 if(chest==null||hand==null||right==null)throw new Exception("Missing humanoid bones");
 var shirt=Mat("Shirt"+kind,kind==0?new Color(.55f,.12f,.09f):kind==1?new Color(.27f,.13f,.42f):new Color(.19f,.27f,.24f));
 foreach(var r in e.GetComponentsInChildren<Renderer>(true)){var ms=r.sharedMaterials;for(int i=0;i<ms.Length;i++)if(ms[i]!=null&&ms[i].name.StartsWith("Shirt"))ms[i]=shirt;r.sharedMaterials=ms;}
 if(kind==0){for(int i=-1;i<=1;i++)Prop(PrimitiveType.Cylinder,chest,new Vector3(i*.15f,.95f,.22f),new Vector3(.11f,.20f,.11f),Mat("Dynamite",new Color(.64f,.09f,.04f)));Prop(PrimitiveType.Cube,chest,new Vector3(0,.91f,.20f),new Vector3(.55f,.09f,.12f),Mat("Strap",Color.black));}
 if(kind==1){Prop(PrimitiveType.Cube,chest,new Vector3(0,1.31f,-.03f),new Vector3(.48f,.08f,.24f),Mat("CasterMantle",new Color(.31f,.13f,.52f)));Prop(PrimitiveType.Sphere,right,right.position+Vector3.forward*.13f,Vector3.one*.17f,Mat("CasterOrb",new Color(.9f,.27f,.04f)));}
 if(kind==2){var pos=hand.position+Vector3.forward*.23f+Vector3.up*.08f;Prop(PrimitiveType.Cube,hand,pos,new Vector3(.82f,1.13f,.12f),Mat("ShieldSteel",new Color(.27f,.34f,.39f)));Prop(PrimitiveType.Cube,hand,pos+Vector3.forward*.08f,new Vector3(.08f,1.10f,.035f),Mat("ShieldCross",new Color(.65f,.65f,.60f)));Prop(PrimitiveType.Cube,hand,pos+Vector3.forward*.08f,new Vector3(.80f,.09f,.035f),Mat("ShieldCross",new Color(.65f,.65f,.60f)));}
 var warning=new GameObject("SpecialWarning",typeof(LineRenderer)).GetComponent<LineRenderer>();warning.transform.SetParent(e.transform,false);warning.sharedMaterial=Mat("WarningRed",new Color(1,.15f,.03f));warning.useWorldSpace=false;warning.loop=true;warning.positionCount=64;warning.widthMultiplier=.045f;for(int j=0;j<64;j++){float t=j*Mathf.PI*2/64;warning.SetPosition(j,new Vector3(Mathf.Cos(t),.025f,Mathf.Sin(t)));}warning.enabled=false;
 var so=new SerializedObject(v);so.FindProperty("survivorWarning").objectReferenceValue=warning;so.ApplyModifiedPropertiesWithoutUndo();
 var name=new[]{"Exploder","Caster","Shield"}[kind];e.name="ArcadeZombie"+name;var path="Assets/Game/Prefabs/Characters/"+e.name+".prefab";PrefabUtility.SaveAsPrefabAsset(e,path);results.Add(new{path,bones=new[]{chest.name,hand.name,right.name},scale=e.transform.localScale});Object.DestroyImmediate(e);
 }
 var bullet="Assets/Game/Prefabs/Combat/GrayboxBullet.prefab";var b=PrefabUtility.LoadPrefabContents(bullet);var bs=new SerializedObject(b.GetComponent<ProjectileView>());bs.FindProperty("survivorHostileFireballTemplate").objectReferenceValue=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Art/Arcade/VFX/FirstBatch/Prefabs/Fireball.prefab");bs.FindProperty("survivorEnemyBlastTemplate").objectReferenceValue=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/ArcadeDroneExplosion.prefab");bs.ApplyModifiedPropertiesWithoutUndo();PrefabUtility.SaveAsPrefabAsset(b,bullet);PrefabUtility.UnloadPrefabContents(b);AssetDatabase.SaveAssets();return results;
}}

