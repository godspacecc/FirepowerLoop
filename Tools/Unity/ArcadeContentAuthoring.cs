using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>前两场纯美术模板制作；只更新 Content 子库，不创建碰撞、规则或构建任务。</summary>
public static class ArcadeContentAuthoring
{
    const string Folder = "Assets/Game/Art/Arcade/Environment/Content";
    const string Bank = "Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab";
    static Material stone, dark, gold, gem, metal, grass, wood;
    static GameObject column, pine, rock, chest, barrel;
    public static string Run()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("仅在编辑态制作");
        Directory.CreateDirectory(Folder + "/Models"); AssetDatabase.Refresh();
        stone = Mat("Sandstone", new Color(.65f,.48f,.28f)); dark = Mat("Slate",new Color(.19f,.23f,.25f));
        gold = Mat("Gold",new Color(1,.68f,.08f)); gem = Mat("Gem",new Color(.08f,.72f,.9f));
        metal = Mat("TowerMetal",new Color(.28f,.36f,.4f)); grass = Mat("ForestGround",new Color(.22f,.3f,.2f));
        wood = Mat("Wood",new Color(.32f,.2f,.1f));
        column=Copy("Assets/Art/PolygonPrototype/Models/SM_Buildings_Column_1x3_01.fbx","Column");
        pine=Copy("Assets/Art/PolygonPrototype/Models/SM_Prop_Tree_Pine_01.fbx","Pine");
        rock=Copy("Assets/Art/PolygonPrototype/Models/SM_Generic_Small_Rocks_01.fbx","Rock");
        chest=Copy("Assets/Art/PolygonPrototype/Models/SM_Prop_Chest_Wood_01.fbx","Chest");
        barrel=Copy("Assets/Art/RPGPP_LT/Models/Props/rpgpp_lt_barrel_01.fbx","Barrel");
        var bank=PrefabUtility.LoadPrefabContents(Bank);
        try
        {
            var old=bank.transform.Find("Content"); if(old!=null) Object.DestroyImmediate(old.gameObject);
            var kit=Node(bank.transform,"Content");
            Arena(kit,"Babylon",false); Arena(kit,"Forest",true); Treasure(kit);
            var gate=Node(kit,"RouteGate");
            for(int side=-1;side<=1;side+=2) Place(column,gate,"Pillar",new Vector3(side*1.8f,0,0),2.6f,stone);
            Shape(gate,"Lintel",new Vector3(0,2.6f,0),new Vector3(4.4f,.4f,.6f),stone);
            Shape(gate,"Threshold",new Vector3(0,.1f,0),new Vector3(3,.15f,2.2f),gold);
            Label(gate,"Destination",new Vector3(0,.35f,-1.8f));
            var entry=Node(kit,"TreasureEntrance"); Place(chest,entry,"Chest",Vector3.zero,1.3f,wood);
            Label(entry,"State",new Vector3(0,.3f,-1.8f));
            Jewel(kit,"GoldBar",false,gold); Jewel(kit,"Gem",true,gem);
            var tower=Node(kit,"ElectricTower");
            Place(column,tower,"Mast",Vector3.zero,2.2f,metal);
            Shape(tower,"Foot",new Vector3(0,.15f,0),new Vector3(.85f,.3f,.85f),dark);
            for(int i=0;i<4;i++) Shape(tower,"Insulator"+i,new Vector3(0,1.3f+i*.22f,0),new Vector3(.8f,.08f,.8f),gold);
            Shape(tower,"Crossbar",new Vector3(0,2.3f,0),new Vector3(1.6f,.18f,.25f),metal);
            foreach(var c in kit.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            kit.gameObject.SetActive(false); PrefabUtility.SaveAsPrefabAsset(bank,Bank); AssetDatabase.SaveAssets();
            return Bank;
        }
        finally {PrefabUtility.UnloadPrefabContents(bank);}
    }
    static void Arena(Transform kit,string name,bool forest)
    {
        var t=Node(kit,name); var ground=forest?grass:stone;
        Shape(t,"Ground",new Vector3(0,.035f,0),new Vector3(39,.06f,39),ground);
        // 外沿装饰不占据战斗区域；北侧保持两条出口通路。
        for(int side=-1;side<=1;side+=2)
        {
            Shape(t,"Boundary",new Vector3(side*20.2f,.4f,0),new Vector3(1.2f,.8f,41),forest?dark:stone);
            for(int i=0;i<7;i++)
            {
                var p=new Vector3(side*21.5f,0,-16+i*5.5f);
                Place(forest?pine:column,t,"Landmark"+side+"_"+i,p,forest?4.8f:3.3f,forest?null:stone);
                Place(rock,t,"Rubble"+side+"_"+i,p+new Vector3(-side,0,1.4f),.8f,dark);
            }
        }
        Shape(t,"SouthBoundary",new Vector3(0,.4f,-20.2f),new Vector3(40,.8f,1.2f),forest?dark:stone);
        for(int i=0;i<6;i++) Place(forest?rock:barrel,t,"SouthDecor"+i,new Vector3(-15+i*6,0,-21.5f),forest?1.3f:1.1f,forest?dark:wood);
        if(!forest)
        {
            for(int z=-1;z<=1;z+=2) Shape(t,"Inlay",new Vector3(0,.075f,z*9),new Vector3(32,.015f,.25f),dark);
            for(int side=-1;side<=1;side+=2) Place(chest,t,"SupplyCache"+side,new Vector3(side*21.5f,0,0),1.2f,wood);
        }
    }
    static void Treasure(Transform kit)
    {
        var t=Node(kit,"TreasureRoom"); Shape(t,"Ground",new Vector3(0,.06f,0),new Vector3(18,.1f,14),dark);
        for(int side=-1;side<=1;side+=2)
        {
            Shape(t,"Boundary",new Vector3(side*9.7f,.5f,0),new Vector3(.8f,1,15),stone);
            Shape(t,"EndBoundary",new Vector3(0,.4f,side*7.7f),new Vector3(19,.8f,.8f),stone);
            for(int i=0;i<3;i++) Place(column,t,"Pillar"+side+"_"+i,new Vector3(side*9.5f,0,-5+i*5),2.8f,stone);
            Place(chest,t,"VaultChest"+side,new Vector3(side*8.7f,0,6.8f),1.2f,wood);
        }
    }
    static void Jewel(Transform kit,string name,bool crystal,Material mat)
    {
        var mesh=new Mesh { name=name };
        if(crystal)
        {
            mesh.vertices=new[]{new Vector3(0,.85f,0),new Vector3(-.4f,.35f,-.3f),new Vector3(.4f,.35f,-.3f),new Vector3(.4f,.35f,.3f),new Vector3(-.4f,.35f,.3f),new Vector3(0,0,0)};
            mesh.triangles=new[]{0,2,1,0,3,2,0,4,3,0,1,4,5,1,2,5,2,3,5,3,4,5,4,1};
        }
        else
        {
            mesh.vertices=new[]{new Vector3(-.5f,0,-.3f),new Vector3(.5f,0,-.3f),new Vector3(.5f,0,.3f),new Vector3(-.5f,0,.3f),new Vector3(-.38f,.3f,-.2f),new Vector3(.38f,.3f,-.2f),new Vector3(.38f,.3f,.2f),new Vector3(-.38f,.3f,.2f)};
            mesh.triangles=new[]{0,1,5,0,5,4,1,2,6,1,6,5,2,3,7,2,7,6,3,0,4,3,4,7,4,5,6,4,6,7,0,3,2,0,2,1};
        }
        mesh.RecalculateNormals(); mesh.RecalculateBounds(); var path=Folder+"/"+name+".asset";
        var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(existing==null) AssetDatabase.CreateAsset(mesh,path); else {EditorUtility.CopySerialized(mesh,existing);Object.DestroyImmediate(mesh);mesh=existing;}
        var t=Node(kit,name); t.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;t.gameObject.AddComponent<MeshRenderer>().sharedMaterial=mat;
    }
    static void Label(Transform parent,string name,Vector3 position)
    {
        var go=new GameObject(name,typeof(TextMeshPro));go.transform.SetParent(parent,false);go.transform.localPosition=position+Vector3.up;go.transform.localRotation=Quaternion.Euler(60,0,0);
        var tmp=go.GetComponent<TextMeshPro>();tmp.font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Game/Fonts/Chinese/SourceHanSansCN-Normal SDF.asset");
        if(tmp.font==null) throw new InvalidOperationException("正式中文字体缺失");
        tmp.fontSize=5;tmp.enableAutoSizing=true;tmp.fontSizeMin=3.5f;tmp.fontSizeMax=5;
        tmp.alignment=TextAlignmentOptions.Center;tmp.color=Color.white;tmp.rectTransform.sizeDelta=new Vector2(4.7f,3);tmp.text="";
    }
    static Transform Node(Transform p,string n){var g=new GameObject(n);g.transform.SetParent(p,false);return g.transform;}
    static Material Mat(string n,Color c){var p=Folder+"/"+n+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(p);if(m==null){m=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(m,p);}m.SetColor("_BaseColor",c);m.SetFloat("_Smoothness",.25f);EditorUtility.SetDirty(m);return m;}
    static GameObject Copy(string source,string name)
    {
        var p=Folder+"/Models/"+name+".fbx";if(!File.Exists(p)&&!AssetDatabase.CopyAsset(source,p))throw new InvalidOperationException(source);
        var imp=(ModelImporter)AssetImporter.GetAtPath(p);foreach(var k in imp.GetExternalObjectMap().Keys)imp.RemoveRemap(k);imp.SaveAndReimport();return AssetDatabase.LoadAssetAtPath<GameObject>(p);
    }
    static void Shape(Transform p,string n,Vector3 pos,Vector3 size,Material m){var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=n;g.transform.SetParent(p,false);g.transform.localPosition=pos;g.transform.localScale=size;g.GetComponent<Renderer>().sharedMaterial=m;Object.DestroyImmediate(g.GetComponent<Collider>());}
    static void Place(GameObject prefab,Transform parent,string n,Vector3 pos,float height,Material mat)
    {
        var g=Object.Instantiate(prefab);g.name=n;g.transform.position=Vector3.zero;var rs=g.GetComponentsInChildren<Renderer>();var b=rs[0].bounds;foreach(var r in rs)b.Encapsulate(r.bounds);
        float s=height/Mathf.Max(.01f,b.size.y);g.transform.SetParent(parent,false);g.transform.localScale*=s;g.transform.localPosition=pos-new Vector3(b.center.x,b.min.y,b.center.z)*s;
        foreach(var r in rs)r.sharedMaterials=r.sharedMaterials.Select(_=>mat??grass).ToArray();
        foreach(var c in g.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(c);
    }
}
