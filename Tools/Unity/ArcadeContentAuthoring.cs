using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
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
            Jewel(kit,"GoldBar",false); Jewel(kit,"Gem",true); RealisticRewards.Piles(kit);
            PickupFlash(kit);
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
    /// <summary>仅原位更新奖励网格、专属材质和领取特效，不重建出口、场景或其他 Content 节点。</summary>
    public static string RefineRewards()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("仅在编辑态制作奖励");
        var bank = PrefabUtility.LoadPrefabContents(Bank);
        try
        {
            var kit = bank.transform.Find("Content");
            if (kit == null) throw new InvalidOperationException("奖励模板库缺失");
            Jewel(kit, "GoldBar", false); Jewel(kit, "Gem", true); RealisticRewards.Piles(kit); PickupFlash(kit);
            PrefabUtility.SaveAsPrefabAsset(bank, Bank); AssetDatabase.SaveAssets();
            return "金条、宝石及领取闪光已原位更新";
        }
        finally { PrefabUtility.UnloadPrefabContents(bank); }
    }

    /// <summary>奖励采用圆润铸金表面与蓝宝石切面；制作逻辑只更新纯美术模板。</summary>
    static void Jewel(Transform kit, string name, bool crystal) => RealisticRewards.Make(kit, name, crystal);

    /// <summary>领取时的短星芒；预先序列化网格、材质与衰减，运行期只发射少量粒子。</summary>
    static void PickupFlash(Transform kit)
    {
        var node = kit.Find("TreasurePickupFlash") ?? Node(kit, "TreasurePickupFlash");
        var ps = node.GetComponent<ParticleSystem>(); if (ps == null) ps = node.gameObject.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main; main.duration = .3f; main.loop = false; main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(.18f, .28f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(.5f, 1.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(.13f, .24f); main.maxParticles = 32;
        main.startColor = new Color(1f, .85f, .42f, 1f); main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = ps.emission; emission.enabled = false;
        var shape = ps.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = .22f;
        var color = ps.colorOverLifetime; color.enabled = true;
        var gradient = new Gradient(); gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
            new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) }); color.color = gradient;
        var size = ps.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 1, 1, .1f));
        var builder = new Facets(1);
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.PI / 4, b = (i + 1) * Mathf.PI / 4;
            float r0 = i % 2 == 0 ? .5f : .13f, r1 = i % 2 == 0 ? .13f : .5f;
            builder.Triangle(Vector3.zero, new Vector3(Mathf.Cos(a) * r0, Mathf.Sin(a) * r0, 0),
                new Vector3(Mathf.Cos(b) * r1, Mathf.Sin(b) * r1, 0), Vector3.back, 0);
        }
        var renderer = ps.GetComponent<ParticleSystemRenderer>(); renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = SaveMesh("TreasureGlint", builder.Build("TreasureGlint"));
        renderer.alignment = ParticleSystemRenderSpace.View;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
        var path = Folder + "/TreasureGlint.mat"; var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) throw new InvalidOperationException("领取粒子 Shader 缺失");
            mat = new Material(shader); AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetColor("_BaseColor", Color.white); mat.SetFloat("_Surface", 1); mat.SetFloat("_Blend", 0);
        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0); mat.SetFloat("_Cull", 0); mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.SetOverrideTag("RenderType", "Transparent"); mat.renderQueue = 3000; EditorUtility.SetDirty(mat);
        renderer.sharedMaterial = mat;
    }

    static Mesh SaveMesh(string name, Mesh mesh)
    {
        var path = Folder + "/" + name + ".asset"; var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null) { AssetDatabase.CreateAsset(mesh, path); return mesh; }
        EditorUtility.CopySerialized(mesh, existing); Object.DestroyImmediate(mesh); EditorUtility.SetDirty(existing); return existing;
    }
    /// <summary>每个切面独立法线，保留低多边形轮廓；面朝向按外向量校正。</summary>
    private sealed class Facets
    {
        private readonly List<Vector3> vertices = new();
        private readonly List<int>[] triangles;
        public Facets(int count) { triangles = Enumerable.Range(0, count).Select(_ => new List<int>()).ToArray(); }
        public void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 outward, int material)
        {
            if (Vector3.Dot(Vector3.Cross(b-a, c-a), outward) < 0) { var swap = b; b = c; c = swap; }
            int start = vertices.Count; vertices.Add(a); vertices.Add(b); vertices.Add(c);
            triangles[material].AddRange(new[] { start, start+1, start+2 });
        }
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward, int material)
        { Triangle(a,b,c,outward,material); Triangle(a,c,d,outward,material); }
        public Mesh Build(string name)
        {
            var mesh = new Mesh { name = name }; mesh.SetVertices(vertices); mesh.subMeshCount = triangles.Length;
            for (int i=0;i<triangles.Length;i++) mesh.SetTriangles(triangles[i],i);
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
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

/// <summary>宝藏奖励的离线建模与表面资源；生成结果直接由模板持有，不在运行期生成网格或纹理。</summary>
internal static class RealisticRewards
{
    const string Folder = "Assets/Game/Art/Arcade/Environment/Content";

    /// <summary>保留既有单体节点与网格 GUID，便于近景检查及组合成领取小堆。</summary>
    public static void Make(Transform kit, string name, bool crystal)
    {
        var mesh = crystal ? Sapphire() : Ingot();
        var node = kit.Find(name); if (node == null) node = Node(kit, name);
        var filter = node.GetComponent<MeshFilter>(); if (filter == null) filter = node.gameObject.AddComponent<MeshFilter>();
        filter.sharedMesh = SaveMesh(name, mesh);
        var renderer = node.GetComponent<MeshRenderer>(); if (renderer == null) renderer = node.gameObject.AddComponent<MeshRenderer>();
        var path = Folder + (crystal ? "/TreasureGem.mat" : "/TreasureGold.mat");
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Game/Shaders/TreasureSurface.shader");
        if (shader == null) throw new InvalidOperationException("写实奖励 Shader 缺失");
        if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
        material.shader = shader;
        material.SetColor("_BaseColor", crystal ? new Color(.012f,.27f,.68f) : new Color(1,.77f,.34f));
        material.SetFloat("_Crystal", crystal ? 1 : 0); material.SetFloat("_Smoothness", crystal ? .98f : .83f);
        material.SetFloat("_Surface", 0); material.SetFloat("_SrcBlend", 1); material.SetFloat("_DstBlend", 0); material.SetFloat("_ZWrite", 1);
        material.SetTexture("_ReflectionCube", Reflection());
        if (!crystal) material.SetTexture("_SurfaceMap", CastSurface());
        renderer.sharedMaterials = new[] { material }; EditorUtility.SetDirty(material);
    }

    /// <summary>三种金条堆和两种宝石堆；尺寸、倾斜与交叠固定，不随网络快照重新随机。</summary>
    public static void Piles(Transform kit)
    {
        for (int variant = 0; variant < 3; variant++) Pile(kit, "GoldPile" + variant, "GoldBar", variant, false);
        for (int variant = 0; variant < 2; variant++) Pile(kit, "GemPile" + variant, "Gem", variant, true);
    }
    static void Pile(Transform kit, string name, string source, int variant, bool crystal)
    {
        var template = kit.Find(source);
        var node = kit.Find(name); if (node == null) node = Node(kit, name);
        int count = crystal ? 2 + variant : 3 + variant;
        // 宝石需要独立对象坐标追踪内部反射；保留子模型，不把多颗宝石烘焙到同一坐标系。
        if (crystal)
        {
            var oldRenderer=node.GetComponent<MeshRenderer>();if(oldRenderer!=null)Object.DestroyImmediate(oldRenderer);
            var oldFilter=node.GetComponent<MeshFilter>();if(oldFilter!=null)Object.DestroyImmediate(oldFilter);
        }
        var combine = new CombineInstance[count];
        for (int i = 0; i < count; i++)
        {
            // 底层围绕拾取中心散落，顶层斜压一枚；整体半径控制在领取范围附近。
            float angle = i * 137.508f + variant * 47;
            float radians = angle * Mathf.Deg2Rad;
            float radius = i == count - 1 ? .12f : (crystal ? .32f : .37f);
            var position = new Vector3(Mathf.Cos(radians)*radius, i == count - 1 ? (crystal ? .17f : .2f) : 0, Mathf.Sin(radians)*radius);
            var rotation = Quaternion.Euler(i == count-1 ? 8 : (i%2==0 ? 2 : -3), angle, i == count-1 ? -7 : 0);
            float scale = crystal ? .7f + i*.045f : .66f + (i%3)*.055f;
            combine[i] = new CombineInstance { mesh = template.GetComponent<MeshFilter>().sharedMesh,
                transform = Matrix4x4.TRS(position, rotation, Vector3.one * scale) };
            if (crystal)
            {
                var child=node.Find("Stone"+i);if(child==null)child=Node(node,"Stone"+i);
                child.localPosition=position+Vector3.up*.025f;child.localRotation=rotation;child.localScale=Vector3.one*scale;
                var childFilter=child.GetComponent<MeshFilter>();if(childFilter==null)childFilter=child.gameObject.AddComponent<MeshFilter>();
                childFilter.sharedMesh=template.GetComponent<MeshFilter>().sharedMesh;
                var childRenderer=child.GetComponent<MeshRenderer>();if(childRenderer==null)childRenderer=child.gameObject.AddComponent<MeshRenderer>();
                childRenderer.sharedMaterials=template.GetComponent<MeshRenderer>().sharedMaterials;
            }
        }
        if(crystal)return;
        var mesh = new Mesh { name = name }; mesh.CombineMeshes(combine, true, true);
        // 最低点归零，避免倾斜模型穿过房间地面。
        var vertices = mesh.vertices; float bottom = mesh.bounds.min.y;
        for (int i = 0; i < vertices.Length; i++) vertices[i].y -= bottom;
        mesh.vertices = vertices; mesh.RecalculateBounds();
        var filter = node.GetComponent<MeshFilter>(); if (filter == null) filter = node.gameObject.AddComponent<MeshFilter>();
        filter.sharedMesh = SaveMesh(name, mesh);
        var renderer = node.GetComponent<MeshRenderer>(); if (renderer == null) renderer = node.gameObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = template.GetComponent<MeshRenderer>().sharedMaterials;
    }

    /// <summary>多圈圆角矩形形成铸造金条的收口与圆润肩部，共享顶点产生连续高光。</summary>
    static Mesh Ingot()
    {
        var rings = new[] { Round(.52f,.275f,.08f,0), Round(.553f,.302f,.095f,.012f),
            Round(.57f,.31f,.105f,.04f), Round(.511f,.278f,.10f,.265f),
            Round(.5f,.267f,.10f,.295f), Round(.483f,.253f,.097f,.32f), Round(.455f,.231f,.09f,.332f) };
        int n = rings[0].Length; var vertices = rings.SelectMany(r => r).ToList(); var triangles = new List<int>();
        void Face(int a,int b,int c,Vector3 outward)
        { if (Vector3.Dot(Vector3.Cross(vertices[b]-vertices[a],vertices[c]-vertices[a]),outward)<0) { int swap=b;b=c;c=swap; } triangles.AddRange(new[]{a,b,c}); }
        for (int row=0;row<rings.Length-1;row++) for (int i=0;i<n;i++)
        {
            int j=(i+1)%n,a=row*n+i,b=(row+1)*n+i,c=(row+1)*n+j,d=row*n+j;
            var outward=vertices[a]; outward.y=0; Face(a,b,c,outward); Face(a,c,d,outward);
        }
        int top=vertices.Count; vertices.Add(new Vector3(0,.332f,0)); int bottom=vertices.Count; vertices.Add(Vector3.zero);
        for(int i=0;i<n;i++) { Face(top,(rings.Length-1)*n+i,(rings.Length-1)*n+(i+1)%n,Vector3.up); Face(bottom,i,(i+1)%n,Vector3.down); }
        var mesh=new Mesh { name="GoldBar" }; mesh.SetVertices(vertices); mesh.SetTriangles(triangles,0);
        mesh.SetUVs(0,vertices.Select(p=>new Vector2(p.x/1.14f+.5f,p.z/.62f+.5f)).ToList());
        mesh.RecalculateNormals();
        var normals=mesh.normals; for(int i=(rings.Length-1)*n;i<=top;i++)normals[i]=Vector3.up;mesh.normals=normals;
        mesh.RecalculateBounds(); return mesh;
    }
    static Vector3[] Round(float x,float z,float radius,float y)
    {
        var list=new List<Vector3>();
        for(int corner=0;corner<4;corner++)
        {
            float a0=corner*90; float cx=corner==0||corner==3?x-radius:-x+radius;
            float cz=corner<2?z-radius:-z+radius;
            for(int step=0;step<6;step++)
            { float a=(a0+step*90f/5)*Mathf.Deg2Rad; list.Add(new Vector3(cx+Mathf.Cos(a)*radius,y,cz+Mathf.Sin(a)*radius)); }
        }
        return list.ToArray();
    }

    /// <summary>枕形蓝宝石拥有独立三角切面法线、窄腰线、冠部和收尖亭部。</summary>
    static Mesh Sapphire()
    {
        const int n=16;
        Vector3 Ring(int index,float x,float z,float y,float offset=0)
        {float angle=(index*360f/n+offset)*Mathf.Deg2Rad;return new Vector3(Mathf.Cos(angle)*x,y,Mathf.Sin(angle)*z);}
        var lower=Enumerable.Range(0,n).Select(i=>Ring(i,.46f,.365f,.24f)).ToArray();
        var upper=Enumerable.Range(0,n).Select(i=>Ring(i,.46f,.365f,.265f)).ToArray();
        var table=Enumerable.Range(0,8).Select(i=>Ring(i*2,.225f,.18f,.49f)).ToArray();
        var vertices=new List<Vector3>(); var triangles=new List<int>();
        void Triangle(Vector3 a,Vector3 b,Vector3 c,Vector3 outward)
        { if(Vector3.Dot(Vector3.Cross(b-a,c-a),outward)<0){var swap=b;b=c;c=swap;} int start=vertices.Count;vertices.AddRange(new[]{a,b,c});triangles.AddRange(new[]{start,start+1,start+2}); }
        for(int i=0;i<n;i++)
        {
            int j=(i+1)%n; var outside=lower[i]+lower[j]; outside.y=0;
            Triangle(Vector3.zero,lower[i],lower[j],outside-Vector3.up);
            Triangle(lower[i],upper[i],upper[j],outside); Triangle(lower[i],upper[j],lower[j],outside);
        }
        for(int i=0;i<8;i++)
        {
            int next=(i+1)%8; int a=i*2,b=(a+1)%n,c=(a+2)%n;
            var star=Ring(a,.34f,.27f,.39f,22.5f);var outward=upper[b];outward.y=.3f;
            Triangle(table[i],table[next],star,outward);
            Triangle(table[i],star,upper[a],outward);
            Triangle(star,upper[b],upper[a],outward);
            Triangle(star,upper[c],upper[b],outward);
            Triangle(star,table[next],upper[c],outward);
            Triangle(new Vector3(0,.49f,0),table[i],table[next],Vector3.up);
        }
        var mesh=new Mesh { name="Gem" }; mesh.SetVertices(vertices); mesh.SetTriangles(triangles,0);
        mesh.SetUVs(0,vertices.Select(p=>new Vector2(p.x+.5f,p.z+.5f)).ToList());mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
    }

    /// <summary>固定的环境反射图为金属和宝石提供明暗条带，避免依赖场景中是否存在反射探针。</summary>
    static Cubemap Reflection()
    {
        string path=Folder+"/TreasureReflection.asset";
        var existing=AssetDatabase.LoadAssetAtPath<Cubemap>(path);
        const int size=128; var cube=new Cubemap(size,TextureFormat.RGBAHalf,true);
        for(int face=0;face<6;face++)
        {
            var colors=new Color[size*size];
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                float u=2*(x+.5f)/size-1,v=2*(y+.5f)/size-1;
                Vector3 direction=face==0?new Vector3(1,-v,-u):face==1?new Vector3(-1,-v,u):face==2?new Vector3(u,1,v):face==3?new Vector3(u,-1,-v):face==4?new Vector3(u,-v,1):new Vector3(-u,-v,-1);
                direction.Normalize();
                float ambient=.2f+.16f*Mathf.Max(0,direction.y);
                float panel=direction.y>0?Rectangle(direction.x/direction.y,direction.z/direction.y,-1.8f,-.6f,.15f,2.2f):0;
                float strip=direction.z>0?Rectangle(direction.x/direction.z,direction.y/direction.z,-.7f,.35f,.7f,1.2f):0;
                float rim=direction.x<0?Rectangle(direction.z/-direction.x,direction.y/-direction.x,-.5f,.1f,.3f,1.5f):0;
                colors[y*size+x]=new Color(ambient+panel*1.5f+strip*1.25f+rim*.55f,ambient+panel*1.48f+strip*1.22f+rim*.65f,ambient+panel*1.42f+strip*1.15f+rim*.8f,1);
            }
            cube.SetPixels(colors,(CubemapFace)face);
        }
        cube.Apply(true,false); cube.wrapMode=TextureWrapMode.Clamp;
        if(existing==null){AssetDatabase.CreateAsset(cube,path);return cube;}
        EditorUtility.CopySerialized(cube,existing);Object.DestroyImmediate(cube);EditorUtility.SetDirty(existing);return existing;
    }
    static float Rectangle(float x,float y,float minX,float minY,float maxX,float maxY)
    { return Mathf.SmoothStep(0,1,Mathf.Clamp01(Mathf.Min(Mathf.Min(x-minX,maxX-x),Mathf.Min(y-minY,maxY-y))/.05f)); }

    /// <summary>离线生成铸造细纹与 FPL/999.9 压印高度，运行时只采样一张表面图。</summary>
    static Texture2D CastSurface()
    {
        string path=Folder+"/TreasureCastSurface.asset"; var prior=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        const int width=1024,height=512; var texture=new Texture2D(width,height,TextureFormat.RGBAHalf,true,true);var pixels=new Color[width*height];
        var segments=new List<Vector4>();
        void Stroke(float ax,float ay,float bx,float by)=>segments.Add(new Vector4(ax,ay,bx,by));
        // 简洁的压印字形：上行 FPL，下行 999.9，笔画用距离场抗锯齿。
        for(int c=0;c<3;c++)
        {
            float x=.37f+c*.095f,y=.54f,w=.06f,h=.15f;
            Stroke(x,y,x,y+h);
            if(c<2){Stroke(x,y+h,x+w,y+h);Stroke(x,y+h*.52f,x+w*.85f,y+h*.52f);}
            if(c==1)Stroke(x+w,y+h*.52f,x+w,y+h);
            if(c==2)Stroke(x,y,x+w,y);
        }
        for(int c=0;c<4;c++)
        {
            float x=.36f+c*.077f,y=.29f,w=.045f,h=.11f;
            Stroke(x,y+h,x+w,y+h);Stroke(x,y+h,x,y+h*.5f);Stroke(x,y+h*.5f,x+w,y+h*.5f);
            Stroke(x+w,y,x+w,y+h);Stroke(x,y,x+w,y);
        }
        Stroke(.577f,.29f,.58f,.29f);
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {
            var p=new Vector2((x+.5f)/width,(y+.5f)/height);float distance=1;
            foreach(var line in segments)
            {var a=new Vector2(line.x,line.y);var b=new Vector2(line.z,line.w);var ab=b-a;float t=Mathf.Clamp01(Vector2.Dot(p-a,ab)/Mathf.Max(.00001f,ab.sqrMagnitude));distance=Mathf.Min(distance,Vector2.Distance(p,a+ab*t));}
            float stamp=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.0025f,.0055f,distance));
            float noise=Mathf.PerlinNoise(x*.012f,y*.016f);float grain=.5f+(noise-.5f)*.025f;
            float scratch=Mathf.Pow(Mathf.Max(0,Mathf.Sin(x*.19f+y*.013f)),60)*.045f;
            pixels[y*width+x]=new Color(grain+scratch*.15f,.5f-stamp*.16f,stamp,1);
        }
        texture.SetPixels(pixels);texture.Apply(true,false);texture.wrapMode=TextureWrapMode.Clamp;
        if(prior==null){AssetDatabase.CreateAsset(texture,path);return texture;}
        EditorUtility.CopySerialized(texture,prior);Object.DestroyImmediate(texture);EditorUtility.SetDirty(prior);return prior;
    }
    static Transform Node(Transform parent,string name){var node=new GameObject(name).transform;node.SetParent(parent,false);return node;}
    static Mesh SaveMesh(string name,Mesh mesh)
    {
        string path=Folder+"/"+name+".asset";var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(existing==null){AssetDatabase.CreateAsset(mesh,path);return mesh;}
        EditorUtility.CopySerialized(mesh,existing);Object.DestroyImmediate(mesh);EditorUtility.SetDirty(existing);return existing;
    }
}
