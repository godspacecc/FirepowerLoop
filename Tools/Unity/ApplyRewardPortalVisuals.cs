using System;
using System.Collections.Generic;
using System.Linq;
using Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
/// <summary>仅制作已于2026-10-22确认的宝箱及传送门，独立场景内保存新资源，不覆盖旧资源。</summary>
public static class ApplyRewardPortalVisuals
{
    public static string Run()
    {
        if(EditorApplication.isPlaying||EditorApplication.isCompiling)throw new InvalidOperationException("仅编辑态制作");
        var prior=SceneManager.GetActiveScene();var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);
            const string folder="Assets/Game/Materials/Arcade";
            if(!AssetDatabase.IsValidFolder(folder))AssetDatabase.CreateFolder("Assets/Game/Materials","Arcade");
            var brown=Material(folder+"/RoundRewardDebris.mat",new Color(.28f,.15f,.06f));
            var bright=Material(folder+"/StagePortalBright.mat",new Color(.2f,1,.75f));
            var dark=Material(folder+"/StagePortalDark.mat",new Color(.03f,.23f,.24f));
            var template=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab").transform.Find("Content");
            var root=new GameObject("ArcadeRoundRewardVisual",typeof(ArcadeRoundRewardVisual));
            var chest=Object.Instantiate(template.Find("TreasureEntrance/Chest").gameObject,root.transform);Fit(chest,1.2f);chest.name="Chest";chest.transform.localPosition=new Vector3(0,4.25f,0);
            var config=AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Config/tbarcaderoundreward.bytes");
            var row=new TbArcadeRoundReward(new Luban.ByteBuf(config.bytes)).DataList[0];int count=row.CoinCount+row.BarCount;
            for(int i=0;i<count;i++)
            {
                bool bar=i*row.BarCount%count<row.BarCount;
                var coin=bar?Object.Instantiate(template.Find("GoldBar").gameObject,root.transform):Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/ArcadeDropCoin.prefab"),root.transform);
                coin.name="Reward"+i;Fit(coin,bar?.5f:.3f);coin.SetActive(false);
            }
            for(int i=0;i<8;i++){var part=Primitive(PrimitiveType.Cube,brown,new Vector3(.23f,.09f,.2f));part.name="Debris"+i;part.transform.SetParent(root.transform,false);part.SetActive(false);}
            Strip(root);PrefabUtility.SaveAsPrefabAsset(root,"Assets/Game/Prefabs/SceneObjects/ArcadeRoundRewardVisual.prefab");Object.DestroyImmediate(root);
            root=new GameObject("ArcadeStagePortalVisual",typeof(ArcadeStagePortalVisual));var door=new GameObject("Portal");door.transform.SetParent(root.transform,false);door.transform.localPosition=new Vector3(0,1.45f,0);
            Circle("PortalOuter",1.3f,.12f,bright,door.transform);Circle("PortalInner",1.14f,.055f,bright,door.transform);
            var disc=Primitive(PrimitiveType.Cylinder,dark,new Vector3(2.26f,.012f,2.26f));disc.transform.SetParent(door.transform,false);disc.transform.localRotation=Quaternion.Euler(90,0,0);
            for(int j=0;j<5;j++){var swirl=new GameObject("Flow"+j,typeof(LineRenderer));swirl.transform.SetParent(door.transform,false);var line=swirl.GetComponent<LineRenderer>();line.useWorldSpace=false;line.sharedMaterial=bright;line.widthMultiplier=.04f;line.positionCount=45;
                for(int i=0;i<45;i++){float r=.1f+i/44f,a=i/44f*4.8f+j*Mathf.PI*.4f;line.SetPosition(i,new Vector3(Mathf.Cos(a)*r,Mathf.Sin(a)*r,-.04f));}}
            PrefabUtility.SaveAsPrefabAsset(root,"Assets/Game/Prefabs/SceneObjects/ArcadeStagePortalVisual.prefab");Object.DestroyImmediate(root);AssetDatabase.SaveAssets();return "已保存两版确认视觉";
        }
        finally{SceneManager.SetActiveScene(prior);EditorSceneManager.CloseScene(scene,true);}
    }
    private static Material Material(string path,Color color)
    {var m=AssetDatabase.LoadAssetAtPath<Material>(path);if(m==null){m=new Material(Shader.Find("Universal Render Pipeline/Unlit"));AssetDatabase.CreateAsset(m,path);}m.color=color;EditorUtility.SetDirty(m);return m;}
    private static void Strip(GameObject root)
    {foreach(var c in root.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(c);foreach(var m in root.GetComponentsInChildren<MonoBehaviour>(true))if(!(m is ArcadeRoundRewardVisual))Object.DestroyImmediate(m);foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=0;}
    private static GameObject Primitive(PrimitiveType type,Material mat,Vector3 scale){var o=GameObject.CreatePrimitive(type);o.transform.localScale=scale;o.GetComponent<Renderer>().sharedMaterial=mat;Object.DestroyImmediate(o.GetComponent<Collider>());return o;}
    private static LineRenderer Circle(string name,float radius,float width,Material mat,Transform parent)
    {var o=new GameObject(name,typeof(LineRenderer));o.transform.SetParent(parent,false);var l=o.GetComponent<LineRenderer>();l.useWorldSpace=false;l.loop=true;l.sharedMaterial=mat;l.widthMultiplier=width;l.positionCount=80;for(int i=0;i<80;i++){float a=i*Mathf.PI*2/80;l.SetPosition(i,new Vector3(Mathf.Cos(a)*radius,Mathf.Sin(a)*radius,0));}return l;}
    private static void Fit(GameObject go,float size){var rs=go.GetComponentsInChildren<Renderer>(true);var b=rs[0].bounds;foreach(var r in rs.Skip(1))b.Encapsulate(r.bounds);go.transform.localScale*=size/Mathf.Max(b.size.x,b.size.y,b.size.z);}
}
