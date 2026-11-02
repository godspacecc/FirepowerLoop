using System;using System.IO;using System.Linq;using System.Collections.Generic;using Game;using UnityEngine;using UnityEditor;using Object=UnityEngine.Object;
public class Script{
 public static object Main(){if(Application.isPlaying)throw new Exception("Edit only");var dir="Assets/Game/Art/Arcade/SpecialZombies/Enchant";Directory.CreateDirectory(dir);AssetDatabase.Refresh();
 var m=new Material(Shader.Find("Universal Render Pipeline/Unlit"));m.color=Color.white;AssetDatabase.CreateAsset(m,dir+"/EnchantRing.mat");
 var go=new GameObject("EnchantRing",typeof(LineRenderer));var ring=go.GetComponent<LineRenderer>();ring.sharedMaterial=m;ring.loop=true;ring.useWorldSpace=false;ring.positionCount=64;ring.widthMultiplier=.045f;ring.enabled=false;
 for(int i=0;i<64;i++){float a=i*Mathf.PI*2/64;ring.SetPosition(i,new Vector3(Mathf.Cos(a),0,Mathf.Sin(a)));}
 var template=PrefabUtility.SaveAsPrefabAsset(go,dir+"/EnchantRing.prefab").GetComponent<LineRenderer>();Object.DestroyImmediate(go);
 var colors=new[]{new Color(.8f,.14f,.22f),new Color(.12f,.65f,.25f),new Color(.15f,.4f,.95f)};var names=new[]{"Health","Speed","Shield"};
 for(int i=0;i<3;i++){
 var body=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/ArcadeZombieCaster.prefab"));body.name="ArcadeZombieEnchant"+names[i];
 var cloth=new Material(Shader.Find("Universal Render Pipeline/Lit"));cloth.color=colors[i];cloth.name="Enchant"+names[i];AssetDatabase.CreateAsset(cloth,dir+"/"+cloth.name+".mat");
 foreach(var r in body.GetComponentsInChildren<Renderer>(true)){var mats=r.sharedMaterials;for(int j=0;j<mats.Length;j++)if(mats[j]!=null&&(mats[j].name.Contains("Mantle")||mats[j].name.Contains("Orb")||mats[j].name.StartsWith("Shirt")))mats[j]=cloth;r.sharedMaterials=mats;}
 var so=new SerializedObject(body.GetComponent<EnemyView>());so.FindProperty("survivorEnchantRingTemplate").objectReferenceValue=template;so.ApplyModifiedPropertiesWithoutUndo();PrefabUtility.SaveAsPrefabAsset(body,"Assets/Game/Prefabs/Characters/"+body.name+".prefab");Object.DestroyImmediate(body);
 }
 var changed=new List<string>();foreach(var guid in AssetDatabase.FindAssets("t:Prefab",new[]{"Assets/Game/Prefabs/Characters"})){
 var path=AssetDatabase.GUIDToAssetPath(guid);var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(prefab.GetComponent<EnemyView>()==null)continue;
 var body=PrefabUtility.LoadPrefabContents(path);var so=new SerializedObject(body.GetComponent<EnemyView>());so.FindProperty("survivorEnchantRingTemplate").objectReferenceValue=template;so.ApplyModifiedPropertiesWithoutUndo();PrefabUtility.SaveAsPrefabAsset(body,path);PrefabUtility.UnloadPrefabContents(body);changed.Add(path);
 }
 AssetDatabase.SaveAssets();return changed;
 }
}
