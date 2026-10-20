using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

/// <summary>在已有海岛模板增补东岸码头和三面岛内植被；不修改碰撞、关卡数值或旧资源。</summary>
public static class ArcadeCoastAuthoring
{
    private const string Island="Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeIslandVisualSet.prefab";
    private const string Bank="Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab";
    public static string Run()
    {
        if(Application.isPlaying||Application.dataPath.Replace('\\','/')!="E:/GitHub/FirepowerLoop/Client/Assets")throw new InvalidOperationException("仅本工程编辑态制作海岸");
        var root=PrefabUtility.LoadPrefabContents(Island);
        try
        {
            var old=root.transform.Find("CoastDecor");if(old!=null)Object.DestroyImmediate(old.gameObject);
            var decor=new GameObject("CoastDecor").transform;decor.SetParent(root.transform,false);
            var wood=Material("CoastWood",new Color(.31f,.20f,.105f));
            var iron=Material("CoastIron",new Color(.24f,.19f,.14f));
            // 码头沿东岸伸出；第一轮只显示这一套装饰，其他轮次仍沿用各自地标。
            for(int i=0;i<18;i++)Box(decor,"PierPlank"+i,new Vector3(12.5f+i*.36f,.18f,5),new Vector3(.32f,.22f,3),wood);
            foreach(float x in new[]{12.5f,15.2f,18.2f})foreach(float z in new[]{3.35f,6.65f})
                Box(decor,"PierPost",new Vector3(x,.43f,z),new Vector3(.28f,1.8f,.28f),wood);
            Box(decor,"PierBrace",new Vector3(15.4f,.05f,3.75f),new Vector3(6.3f,.28f,.22f),wood);
            Box(decor,"PierBrace",new Vector3(15.4f,.05f,6.25f),new Vector3(6.3f,.28f,.22f),wood);
            Crate(decor,new Vector3(11.8f,.6f,8.4f),wood,iron);
            Crate(decor,new Vector3(13.3f,.6f,-6.5f),wood,iron);
            // 外围物件位于实际行走轮廓之外，三条出口车道留空，不制造可见无碰撞的场内墙。
            var region=new FirepowerLoop.Arcade.ArcadeIslandRegion(19.5f,19.5f,1);
            var rock=root.transform.Find("ShoreDecor/Rock0");var palm=root.transform.Find("ShoreDecor/Palm0");
            if(rock==null||palm==null)throw new InvalidOperationException("现有岩石或棕榈模板缺失");
            for(int i=8;i<89;i+=3)
            {
                var edge=region.Outer[i];var p=new Vector3(edge.X*1.12f,.02f,edge.Y*1.12f);
                if((p.x<0&&Mathf.Abs(p.z)<4.2f)||(Mathf.Abs(p.x)<4.2f&&Mathf.Abs(p.z)>13))continue;
                Place(rock,decor,"CoastRock"+i,p,new Vector3(1.8f,1.8f,1.8f),i*37);
                if(i%2==0)Place(palm,decor,"CoastPalm"+i,p+new Vector3(-.9f,0,1),Vector3.one,i*51);
            }
            decor.gameObject.SetActive(false);
            PrefabUtility.SaveAsPrefabAsset(root,Island);
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
        // 运行时从已登记集合的 Island 子模板加载，保留集合其余对象及已有地址。
        var bank=PrefabUtility.LoadPrefabContents(Bank);
        try
        {
            var prior=bank.transform.Find("Island");int index=prior.GetSiblingIndex();Object.DestroyImmediate(prior.gameObject);
            var instance=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Island),bank.transform);
            instance.name="Island";instance.SetActive(false);instance.transform.SetSiblingIndex(index);
            PrefabUtility.SaveAsPrefabAsset(bank,Bank);
        }
        finally{PrefabUtility.UnloadPrefabContents(bank);}
        AssetDatabase.SaveAssets();return "已更新正式海岸装饰与运行时模板";
    }
    private static Material Material(string name,Color color)
    {
        string path="Assets/Game/Art/Arcade/Island/Materials/"+name+".mat";
        var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(m==null){m=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(m,path);}
        m.color=color;m.SetFloat("_Smoothness",.08f);EditorUtility.SetDirty(m);return m;
    }
    private static void Box(Transform owner,string name,Vector3 position,Vector3 size,Material material)
    {
        var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(owner,false);
        go.transform.localPosition=position;go.transform.localScale=size;go.GetComponent<Renderer>().sharedMaterial=material;
        Object.DestroyImmediate(go.GetComponent<Collider>());
    }
    private static void Crate(Transform owner,Vector3 position,Material wood,Material iron)
    {
        Box(owner,"CoastCrate",position,new Vector3(1.15f,1.2f,1.15f),wood);
        foreach(float x in new[]{-.4f,.4f})Box(owner,"CrateBand",position+new Vector3(x,0,0),new Vector3(.08f,1.22f,1.17f),iron);
    }
    private static void Place(Transform template,Transform owner,string name,Vector3 position,Vector3 scale,float yaw)
    {
        var copy=Object.Instantiate(template.gameObject,owner,false);copy.name=name;copy.SetActive(true);
        copy.transform.localPosition=position;copy.transform.localScale=Vector3.Scale(template.localScale,scale);copy.transform.localRotation=Quaternion.Euler(0,yaw,0);
        foreach(var c in copy.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(c);
    }
}
