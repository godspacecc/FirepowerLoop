using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

/// <summary>正式资源的隔离布置预览，不运行规则、不修改玩家场景或存档。</summary>
public static class ArcadeContentPreview
{
    public static string Run()
    {
        var prior=SceneManager.GetActiveScene();var priorRt=RenderTexture.active;
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);SceneManager.SetActiveScene(scene);
        var kit=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab").transform.Find("Content");
        var text=JsonConvert.DeserializeObject<Dictionary<string,string>>(File.ReadAllText("Assets/Game/Config/Localization/zh-CN.json"));
        try
        {
            var camera=new GameObject("PreviewCamera",typeof(Camera)).GetComponent<Camera>();
            camera.transform.position=new Vector3(0,42,-26);camera.transform.LookAt(Vector3.zero);camera.orthographic=true;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.08f,.11f,.14f);
            var light=new GameObject("PreviewLight",typeof(Light)).GetComponent<Light>();light.type=LightType.Directional;light.intensity=1.8f;light.transform.rotation=Quaternion.Euler(55,-30,0);
            foreach(var name in new[]{"Babylon","Forest","TreasureRoom"})
            {
                IDisposable hazard=null;
                var root=new GameObject(name+"Preview");var template=kit.Find(name);
                var env=Object.Instantiate(template.gameObject,root.transform,false);env.SetActive(true);
                camera.orthographicSize=name=="TreasureRoom"?12:29;
                if(name=="TreasureRoom")
                {
                    for(int i=0;i<12;i++)Place(kit,"GoldBar",root.transform,new Vector3(-6+i%4*4,.14f,i/4*2));
                    for(int i=0;i<4;i++)Place(kit,"Gem",root.transform,new Vector3(-6+i*4,.14f,-2));
                    Gate(kit,root.transform,new Vector3(0,0,-5.5f),text["ui.arcade.treasure.return"]);
                }
                else
                {
                    Gate(kit,root.transform,new Vector3(-2.5f,0,16.5f),text["ui.arcade.destination.brute"]);
                    Gate(kit,root.transform,new Vector3(2.5f,0,16.5f),text["ui.arcade.destination.mixed"]);
                    if(name=="Babylon")
                    {
                        var type=typeof(Game.SurvivorCombatViewController).Assembly.GetType("Game.SurvivorHazardPresentation");
                        hazard=(IDisposable)Activator.CreateInstance(type,root.transform,kit.Find("ElectricTower").gameObject);
                        type.GetMethod("Sync").Invoke(hazard,new object[]{new[]{new FirepowerLoop.Arcade.ArcadeHazardParameters(1,1,-5,0,0,1.2f,0,0,4,1.5f,3,0,1,1,.05f),new FirepowerLoop.Arcade.ArcadeHazardParameters(2,1,5,0,0,1.2f,0,0,4,1.5f,3,0,1,1,.05f)},6d});
                    }
                }
                foreach(var t in root.GetComponentsInChildren<TextMeshPro>(true))t.ForceMeshUpdate(true,true);
                var rt=new RenderTexture(1400,1000,24);var tex=new Texture2D(1400,1000,TextureFormat.RGB24,false);
                try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1400,1000),0,0);tex.Apply();File.WriteAllBytes("Assets/Tests/Results~/街机内容预览-"+name+"-20261013.png",tex.EncodeToPNG());}
                finally{camera.targetTexture=null;RenderTexture.active=priorRt;hazard?.Dispose();Object.DestroyImmediate(tex);Object.DestroyImmediate(rt);Object.DestroyImmediate(root);}
            }
            return "正式资源隔离预览已输出；不是实际游玩截图";
        }
        finally{RenderTexture.active=priorRt;SceneManager.SetActiveScene(prior);EditorSceneManager.CloseScene(scene,true);}
    }
    static GameObject Place(Transform kit,string name,Transform parent,Vector3 p){var go=Object.Instantiate(kit.Find(name).gameObject,parent,false);go.transform.localPosition=p;go.SetActive(true);return go;}
    static void Gate(Transform kit,Transform parent,Vector3 p,string text){var go=Place(kit,"RouteGate",parent,p);go.GetComponentInChildren<TextMeshPro>().text=text;}
}
