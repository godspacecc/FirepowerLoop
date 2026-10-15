using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>制作独立河岸营地美术样片，保存真实几何与资源引用；不接管随机布局、采集、导航或存档。</summary>
public static class ValleyArtSampleAuthoring
{
    private const string Art = "Assets/Game/Art/Environment/CivilisationExpedition/ArtSample";
    private const string ScenePath = "Assets/Game/Scenes/ValleyArtSample.unity";
    private const string Output = "Assets/Tests/Results~/ValleyArtSample";
    private const string Nature = "Assets/Game/Art/Environment/CivilisationExpedition/";
    private static readonly Vector3 Camp = new Vector3(-19, 1.35f, 0);
    // 固定样片种子；每次重做重置，避免编辑器会话内多次调用改变已验收构图。
    private static System.Random Random = new System.Random(61010);
    private static float R(float min, float max) => min + (max-min)*(float)Random.NextDouble();
    private static float RiverX(float z) => 5 + 16*Mathf.Sin(z*.026f) + 5*Mathf.Sin(z*.061f);
    private static float Width(float z) => 10 + 2*Mathf.Sin(z*.043f);

    /// <summary>只用于样片的雕刻高度；不改变正式河谷生成器或玩家保存的高度数组。</summary>
    private static float Height(float x, float z)
    {
        var d = Mathf.Abs(x-RiverX(z)); var w = Width(z);
        if(d < w-1) return -.9f;
        var bank = Mathf.SmoothStep(-.9f, 1.1f, Mathf.InverseLerp(w-1,w+5,d));
        var hills = Mathf.Pow(Mathf.Max(0,d-w-8)*.025f,1.4f) * (2+3*Mathf.PerlinNoise(x*.017f+22,z*.016f+7));
        var ground = bank + hills + .55f*Mathf.PerlinNoise(x*.09f+5,z*.07f+9);
        var clearing = Vector2.Distance(new Vector2(x,z),new Vector2(Camp.x,Camp.z));
        return Mathf.Lerp(Camp.y,ground,Mathf.SmoothStep(0,1,Mathf.InverseLerp(7,15,clearing)));
    }
    private static void Folder(string p)
    {
        if(AssetDatabase.IsValidFolder(p))return;
        var split=p.LastIndexOf('/'); Folder(p.Substring(0,split)); AssetDatabase.CreateFolder(p.Substring(0,split),p.Substring(split+1));
    }
    private static T Save<T>(T asset,string name) where T:Object
    {
        var path=Art+"/"+name;var old=AssetDatabase.LoadAssetAtPath<T>(path);
        if(old==null){AssetDatabase.CreateAsset(asset,path);return asset;}
        if(asset is Mesh source && old is Mesh target)
        {
            // 修改已导入Mesh时同步刷新原生顶点缓冲，避免CPU高度更新而画面继续绘制旧山坡。
            target.Clear();target.indexFormat=source.indexFormat;target.vertices=source.vertices;target.normals=source.normals;target.colors=source.colors;
            for(int channel=0;channel<2;channel++){var data=new List<Vector4>();source.GetUVs(channel,data);target.SetUVs(channel,data);}
            target.subMeshCount=source.subMeshCount;for(int i=0;i<source.subMeshCount;i++)target.SetTriangles(source.GetTriangles(i),i);
            target.bounds=source.bounds;target.UploadMeshData(false);
        }
        else EditorUtility.CopySerialized(asset,old);
        EditorUtility.SetDirty(old);Object.DestroyImmediate(asset);return old;
    }
    private static Material Mat(string name,Color color,bool unlit=false)
    {
        var m=new Material(Shader.Find(unlit?"Universal Render Pipeline/Unlit":"Universal Render Pipeline/Lit")){name=name};
        m.SetColor("_BaseColor",color);m.SetFloat("_Smoothness",.08f);m.SetFloat("_Cull",0);
        return Save(m,name+".mat");
    }
    private static GameObject MeshGo(Transform parent,string name,Mesh mesh,Material material,bool collider=false)
    {
        var go=new GameObject(name);go.transform.SetParent(parent,false);
        go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=material;
        if(collider)go.AddComponent<MeshCollider>().sharedMesh=mesh;return go;
    }
    private static GameObject Place(Transform parent,GameObject source,string name,Vector3 p,float scale)
    {
        var go=(GameObject)PrefabUtility.InstantiatePrefab(source);go.name=name;go.transform.SetParent(parent,false);
        go.transform.position=p;go.transform.rotation=Quaternion.Euler(0,R(0,360),0);go.transform.localScale=Vector3.one*scale;
        return go;
    }
    private static Mesh Mesh(string name,List<Vector3> vertices,List<int> indices)
    {
        var mesh=new Mesh{name=name,indexFormat=IndexFormat.UInt32};mesh.SetVertices(vertices);mesh.SetTriangles(indices,0);
        mesh.RecalculateNormals();mesh.RecalculateBounds();return Save(mesh,name+".asset");
    }
    private static void Tri(List<Vector3> v,List<int> t,Vector3 a,Vector3 b,Vector3 c)
    {var n=v.Count;v.Add(a);v.Add(b);v.Add(c);t.Add(n);t.Add(n+1);t.Add(n+2);}

    /// <summary>显式烘焙地形与浅岸材质，不使用运行时生成脚本；单一地形碰撞用于编辑器走查。</summary>
    private static void Ground(Transform parent,Material[] mats)
    {
        var v=new List<Vector3>();var groups=mats.Select(_=>new List<int>()).ToArray();
        for(int z=-60;z<420;z+=3)for(int x=-150;x<150;x+=3)
        {
            var a=new Vector3(x,Height(x,z),z);var b=new Vector3(x,Height(x,z+3),z+3);
            var c=new Vector3(x+3,Height(x+3,z),z);var d=new Vector3(x+3,Height(x+3,z+3),z+3);
            void Add(Vector3 p,Vector3 q,Vector3 r)
            {
                var mid=(p+q+r)/3;var bank=Mathf.Abs(mid.x-RiverX(mid.z))<Width(mid.z)+4;
                var clear=Vector2.Distance(new Vector2(mid.x,mid.z),new Vector2(Camp.x,Camp.z))<6;
                var index=bank||clear?4:Mathf.Clamp((int)(Mathf.PerlinNoise(mid.x*.13f+6,mid.z*.14f+3)*4),0,3);
                Tri(v,groups[index],p,q,r);
            }
            Add(a,b,c);Add(c,b,d);
        }
        var mesh=new Mesh{name="SculptedRiverbank",indexFormat=IndexFormat.UInt32};mesh.SetVertices(v);mesh.subMeshCount=mats.Length;
        for(int i=0;i<groups.Length;i++)mesh.SetTriangles(groups[i],i);
        mesh.RecalculateNormals();mesh.RecalculateBounds();mesh=Save(mesh,"SculptedRiverbank.asset");
        var go=MeshGo(parent,"SculptedRiverbank",mesh,mats[0],true);go.GetComponent<MeshRenderer>().sharedMaterials=mats;
    }
    private static void River(Transform parent,Material water,Material foam)
    {
        var v=new List<Vector3>();var t=new List<int>();var uv=new List<Vector2>();
        for(int z=-60;z<420;z+=2)
        {
            var n=v.Count;var a=RiverX(z);var b=RiverX(z+2);
            v.Add(new Vector3(a-Width(z),.02f,z));v.Add(new Vector3(a+Width(z),.02f,z));
            v.Add(new Vector3(b-Width(z+2),.02f,z+2));v.Add(new Vector3(b+Width(z+2),.02f,z+2));
            t.AddRange(new[]{n,n+2,n+1,n+1,n+2,n+3});uv.AddRange(new[]{new Vector2(0,z/12f),new Vector2(1,z/12f),new Vector2(0,(z+2)/12f),new Vector2(1,(z+2)/12f)});
        }
        // Poseidon 的 UV0/颜色携带同一三角形另外两顶点，UV1携带流向；普通UV会产生错误法线。
        var packed=new List<Vector3>();var next=new List<Vector4>();var colors=new List<Color>();var flow=new List<Vector4>();var indices=new List<int>();
        for(int i=0;i<t.Count;i+=3)for(int j=0;j<3;j++)
        {var p=v[t[i+j]];var q=v[t[i+(j+1)%3]];var r=v[t[i+(j+2)%3]];indices.Add(packed.Count);packed.Add(p);next.Add(new Vector4(q.x,q.y,q.z,0));colors.Add(new Color(r.x,r.y,r.z,1));flow.Add(new Vector4(0,1,0,1));}
        var mesh=new Mesh{name="WindingWater"};mesh.SetVertices(packed);mesh.SetTriangles(indices,0);mesh.SetUVs(0,next);mesh.SetUVs(1,flow);mesh.SetColors(colors);mesh.RecalculateNormals();mesh.RecalculateBounds();
        MeshGo(parent,"WindingWater",Save(mesh,"WindingWater.asset"),water);
        // 少量断续薄片表达流水方向；不额外增加碰撞或完整高成本水体模拟。
        var fv=new List<Vector3>();var ft=new List<int>();
        for(int i=0;i<150;i++)
        {
            float z=R(-45,330),x=RiverX(z)+R(-.9f,.9f)*Width(z),length=R(.4f,2.6f),w=R(.035f,.10f);
            var p=new Vector3(x,.06f,z);Tri(fv,ft,p+new Vector3(-w,0,0),p+new Vector3(0,0,length),p+new Vector3(w,0,0));
        }
        MeshGo(parent,"WaterGlints",Mesh("WaterGlints",fv,ft),foam).GetComponent<MeshRenderer>().shadowCastingMode=ShadowCastingMode.Off;
    }
    /// <summary>远景只提供山体轮廓与颜色层次，无碰撞；几何固定烘焙，不侵入玩法地图的权威数据。</summary>
    private static void Mountain(Transform parent,int index,Vector3 center,float radius,float high,Material rock,Material snow)
    {
        var v=new List<Vector3>();var stone=new List<int>();var white=new List<int>();const int sides=23;
        var rings=new Vector3[9,sides];
        var folds=Enumerable.Range(0,sides).Select(i=>i%3==0?R(.67f,.8f):R(.95f,1.12f)).ToArray();
        for(int ring=0;ring<9;ring++)for(int i=0;i<sides;i++)
        {
            float angle=i*Mathf.PI*2/sides;float rad=radius*(1-ring*.11f)*folds[i]*R(.93f,1.07f);
            rings[ring,i]=new Vector3(Mathf.Cos(angle)*rad-radius*.08f*ring/8,high*ring*.107f+R(-high*.065f,high*.065f),Mathf.Sin(angle)*rad);
        }
        for(int ring=0;ring<8;ring++)for(int i=0;i<sides;i++)
        {int j=(i+1)%sides;var list=ring>=6&&i%3!=0?white:stone;Tri(v,list,rings[ring,i],rings[ring+1,j],rings[ring+1,i]);Tri(v,list,rings[ring,i],rings[ring,j],rings[ring+1,j]);}
        for(int i=0;i<sides;i++)Tri(v,white,rings[8,i],rings[8,(i+1)%sides],new Vector3(-radius*.09f,high,radius*.04f));
        var mesh=new Mesh{name="Ridge_"+index};mesh.SetVertices(v);mesh.subMeshCount=2;mesh.SetTriangles(stone,0);mesh.SetTriangles(white,1);mesh.RecalculateNormals();mesh.RecalculateBounds();
        var go=MeshGo(parent,"Ridge_"+index,Save(mesh,"Ridge_"+index+".asset"),rock);go.GetComponent<MeshRenderer>().sharedMaterials=new[]{rock,snow};go.transform.position=center;
    }
    private static void Pole(Transform parent,string name,Vector3 a,Vector3 b,float radius,Material wood)
    {
        var go=GameObject.CreatePrimitive(PrimitiveType.Cylinder);go.name=name;go.transform.SetParent(parent,false);
        Object.DestroyImmediate(go.GetComponent<Collider>());go.transform.position=(a+b)/2;go.transform.up=(b-a).normalized;
        go.transform.localScale=new Vector3(radius*2,(b-a).magnitude/2,radius*2);go.GetComponent<MeshRenderer>().sharedMaterial=wood;
    }
    private static void Shelter(Transform parent,Material hide,Material wood)
    {
        var root=new GameObject("BranchAndHideShelter");root.transform.SetParent(parent,false);
        var v=new List<Vector3>();var t=new List<int>();
        // 两侧独立三角面给兽皮轻微不规则起伏；正面保持开口，后面有围挡。
        for(int side=-1;side<=1;side+=2)
        {
            var a=Camp+new Vector3(side*2.5f,0,0);var b=Camp+new Vector3(side*2.3f,0,5);
            var c=Camp+new Vector3(0,3.5f,0);var d=Camp+new Vector3(0,3.1f,5);var mid=(a+b+c+d)/4+new Vector3(0,.18f,0);
            Tri(v,t,a,c,mid);Tri(v,t,c,d,mid);Tri(v,t,d,b,mid);Tri(v,t,b,a,mid);
            Pole(root.transform,"ForkedFrontPole",a,c+new Vector3(-side*.45f,.55f,0),.09f,wood);
            Pole(root.transform,"ForkedBackPole",b,d+new Vector3(-side*.4f,.5f,0),.09f,wood);
            Pole(root.transform,"HideEdge",a,b,.055f,wood);
        }
        Tri(v,t,Camp+new Vector3(-2.3f,0,5),Camp+new Vector3(0,3.1f,5),Camp+new Vector3(2.3f,0,5));
        Pole(root.transform,"RidgeBranch",Camp+new Vector3(0,3.5f,-.4f),Camp+new Vector3(0,3.1f,5.4f),.12f,wood);
        MeshGo(root.transform,"HideCover",Mesh("HideCover",v,t),hide);
        var patches=new List<Vector3>();var seams=new List<int>();
        for(int side=-1;side<=1;side+=2)for(int row=0;row<3;row++)for(int col=0;col<5;col++)
        {
            float u=.12f+row*.25f,z=.35f+col*.92f;
            var a=Camp+new Vector3(side*2.5f*(1-u),3.5f*u+.04f,z);
            var b=a+new Vector3(-side*.24f,.33f,0);var c=a+new Vector3(0,-.06f,.65f);var d=b+new Vector3(0,-.06f,.65f);
            Tri(patches,seams,a,b,c);Tri(patches,seams,c,b,d);
        }
        MeshGo(root.transform,"HideRepairs",Mesh("HideRepairs",patches,seams),Mat("HideRepairs",new Color(.44f,.32f,.20f)));
        PrefabUtility.SaveAsPrefabAsset(root,Art+"/BranchAndHideShelter.prefab");
    }
    /// <summary>将无交互草灌按材质烘焙合批，减少样片绘制对象；不对树木LOD或地形碰撞做合并。</summary>
    private static void BatchCover(Transform root)
    {
        var groups=new Dictionary<Material,List<CombineInstance>>();
        foreach(Transform item in root)
        {
            var lod=item.GetComponentInChildren<LODGroup>();var renderers=lod!=null?lod.GetLODs().Last().renderers:item.GetComponentsInChildren<Renderer>();
            foreach(var r in renderers)
            {
                var filter=r.GetComponent<MeshFilter>();if(filter==null)continue;
                for(int i=0;i<filter.sharedMesh.subMeshCount;i++)
                {
                    var mat=r.sharedMaterials[i];if(!groups.TryGetValue(mat,out var group))groups[mat]=group=new List<CombineInstance>();
                    group.Add(new CombineInstance{mesh=filter.sharedMesh,subMeshIndex=i,transform=root.worldToLocalMatrix*filter.transform.localToWorldMatrix});
                }
            }
        }
        foreach(var item in root.Cast<Transform>().ToArray())Object.DestroyImmediate(item.gameObject);
        int index=0;foreach(var group in groups)
        {
            var mesh=new Mesh{name="MeadowBatch_"+index,indexFormat=IndexFormat.UInt32};mesh.CombineMeshes(group.Value.ToArray(),true,true);mesh.RecalculateBounds();
            MeshGo(root,mesh.name,Save(mesh,mesh.name+".asset"),group.Key);index++;
        }
    }
    private static void Fire(Transform parent,GameObject rock,Material wood,Material flame)
    {
        var fire=Camp+new Vector3(4,0,-3);
        for(int i=0;i<11;i++){var a=i*Mathf.PI*2/11;Place(parent,rock,"HearthStone",fire+new Vector3(Mathf.Cos(a)*.7f,0,Mathf.Sin(a)*.7f),R(.5f,.8f));}
        for(int i=0;i<4;i++){var a=i*Mathf.PI/4;Pole(parent,"Firewood",fire+new Vector3(Mathf.Cos(a)*.6f,.15f,Mathf.Sin(a)*.6f),fire-new Vector3(Mathf.Cos(a)*.6f,-.15f,Mathf.Sin(a)*.6f),.10f,wood);}
        var fv=new List<Vector3>();var ft=new List<int>();
        for(int i=0;i<7;i++){var a=i*Mathf.PI*2/7;Tri(fv,ft,fire+new Vector3(Mathf.Cos(a)*.25f,.2f,Mathf.Sin(a)*.25f),fire+new Vector3(Mathf.Cos(a+.9f)*.3f,.25f,Mathf.Sin(a+.9f)*.3f),fire+new Vector3(R(-.15f,.15f),R(.65f,1.2f),R(-.15f,.15f)));}
        MeshGo(parent,"FlameSilhouette",Mesh("FlameSilhouette",fv,ft),flame).GetComponent<MeshRenderer>().shadowCastingMode=ShadowCastingMode.Off;
        var light=new GameObject("CampfireGlow",typeof(Light));light.transform.SetParent(parent,false);light.transform.position=fire+Vector3.up*.6f;
        var l=light.GetComponent<Light>();l.type=LightType.Point;l.color=new Color(1,.48f,.15f);l.intensity=2;l.range=5;
        Pole(parent,"SeatingLog",Camp+new Vector3(2,.3f,-5),Camp+new Vector3(4.7f,.3f,-6),.25f,wood);
        Pole(parent,"SpareBranch",Camp+new Vector3(6,.2f,2),Camp+new Vector3(8,.2f,1),.17f,wood);
    }
    private static void Clouds(Transform root)
    {
        var material=Mat("CloudPearl",new Color(.83f,.85f,.82f),true);
        for(int index=0;index<10;index++)
        {
            var v=new List<Vector3>();var t=new List<int>();var center=new Vector3(R(-500,500),R(120,200),R(350,700));
            var width=R(24,65);var h=R(4,11);var depth=R(8,18);
            var points=new[]{new Vector3(-width,0,0),new Vector3(-width*.55f,h*.6f,0),new Vector3(-width*.22f,h,depth*.2f),new Vector3(width*.1f,h*.55f,-depth*.4f),new Vector3(width*.45f,h*.75f,0),new Vector3(width,0,0)};
            for(int i=0;i<points.Length-1;i++){Tri(v,t,points[i],new Vector3(0,-h*.3f,depth),points[i+1]);Tri(v,t,points[i],points[i+1],new Vector3(0,-h*.12f,-depth));}
            MeshGo(root,"Cloud_"+index,Mesh("Cloud_"+index,v,t),material).transform.position=center;
        }
    }

    /// <summary>营地外围、树下和河岸生成高低混合草簇；营地入口与水面留白，全部为无碰撞静态表现。</summary>
    private static void Meadow(Transform root)
    {
        var bush=AssetDatabase.LoadAssetAtPath<GameObject>(Nature+"Vegetation/Bush_03.prefab");
        var grass=AssetDatabase.LoadAssetAtPath<GameObject>(Nature+"Vegetation/Grass_02.prefab");
        for(int i=0;i<1100;i++)
        {
            float x=R(-48,48),z=R(-22,100);
            if(!GrassAllowed(x,z))continue;
            Place(root,i%9==0?bush:grass,"GroundCover_"+i,new Vector3(x,Height(x,z),z),i%9==0?R(.7f,1.25f):R(2,4));
        }
        BatchCover(root);
        var materials=new[]{Mat("GrassDeep",new Color(.26f,.34f,.10f)),Mat("GrassFresh",new Color(.32f,.40f,.13f)),Mat("GrassWarm",new Color(.38f,.43f,.16f))};
        var vertices=materials.Select(_=>new List<Vector3>()).ToArray();var triangles=materials.Select(_=>new List<int>()).ToArray();
        // 每个簇有共享中心与高度，簇间留空；宽叶底层和弯曲尖叶上层避免均匀撒点的稀疏感。
        for(int cluster=0;cluster<470;cluster++)
        {
            float cx=R(-45,45),cz=R(-20,95);
            if(!GrassAllowed(cx,cz))continue;
            float radius=R(.5f,1.5f),height=R(.24f,.60f);
            for(int leaf=0;leaf<30;leaf++)
            {
                float angle=R(0,Mathf.PI*2),r=radius*Mathf.Sqrt(R(0,1));
                float x=cx+Mathf.Cos(angle)*r,z=cz+Mathf.Sin(angle)*r;
                if(!GrassAllowed(x,z))continue;
                var p=new Vector3(x,Height(x,z)+.025f,z);var direction=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
                var across=Vector3.Cross(Vector3.up,direction)*R(.055f,.14f);
                float h=height*R(.65f,1.2f);var knee=p+Vector3.up*h*.55f+direction*h*.20f;var tip=p+Vector3.up*h+direction*h*.55f;
                var i=leaf%3;Tri(vertices[i],triangles[i],p-across,p+across,knee+across*.5f);
                Tri(vertices[i],triangles[i],p-across,knee+across*.5f,knee-across*.5f);
                Tri(vertices[i],triangles[i],knee-across*.5f,knee+across*.5f,tip);
            }
        }
        for(int i=0;i<materials.Length;i++)
        {
            var mesh=Mesh("GrassClusters_"+i,vertices[i],triangles[i]);
            // 低矮叶片采用朝上的柔和法线，避免双面草叶背面形成近黑色尖刺。
            mesh.normals=mesh.normals.Select(n=>(Vector3.up*.85f+n*.15f).normalized).ToArray();EditorUtility.SetDirty(mesh);
            MeshGo(root,"GrassClusters_"+i,mesh,materials[i]);
        }
    }
    private static bool GrassAllowed(float x,float z)
    {
        if(Mathf.Abs(x-RiverX(z))<Width(z)+2.5f)return false;
        var d=Vector2.Distance(new Vector2(x,z),new Vector2(Camp.x,Camp.z));
        if(d<5.6f)return false;
        // 从棚前到岸边留出约两米宽的走查空间，火堆与坐木周围不长高草。
        if(z>-5&&z<-2&&x>Camp.x&&x<RiverX(z)-Width(z)-2)return false;
        return true;
    }
    /// <summary>只添加样片石景：水中石块按水位埋入，地面碎石按雕刻高度贴地；营地入口保持留白。</summary>
    private static void DressingRocks(Transform root)
    {
        var source=AssetDatabase.LoadAssetAtPath<GameObject>(Nature+"Sources/SimpleNaturePack/Prefabs/Rock_01.prefab");
        var materials=new[]{Mat("WetSlate",new Color(.38f,.42f,.43f)),Mat("LandSlate",new Color(.45f,.46f,.42f)),Mat("PaleStone",new Color(.52f,.51f,.46f))};
        var foamVertices=new List<Vector3>();var foamTriangles=new List<int>();
        void Add(string name,float x,float z,float scale,bool water,int variant)
        {
            var height=water?-.18f-scale*.10f:Height(x,z)-scale*.045f;
            var go=Place(root,source,name,new Vector3(x,height,z),scale);
            // 同一石块源以横向比例、埋入深度和旋转形成大小形状变化，避免整齐复制。
            go.transform.localScale=Vector3.Scale(go.transform.localScale,new Vector3(R(.8f,1.3f),R(.7f,1.15f),R(.7f,1.25f)));
            foreach(var renderer in go.GetComponentsInChildren<MeshRenderer>(true))renderer.sharedMaterial=materials[variant%materials.Length];
            var bounds=go.GetComponentInChildren<Renderer>().bounds;
            // 原石块枢轴接近中心，按真实包围盒安放，防止地面石块埋掉半身、水中主石仅剩薄顶。
            go.transform.position+=Vector3.up*(water?Mathf.Clamp(scale*.27f,.15f,1.5f)-bounds.max.y:Height(x,z)-scale*.06f-bounds.min.y);
            if(!water||scale<2)return;
            float radius=scale*.4f;var center=new Vector3(x,.065f,z);
            for(int i=0;i<15;i++)
            {
                if(i%5==0)continue;float angle=i*Mathf.PI*2/18,next=(i+1)*Mathf.PI*2/18;
                var a=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));var b=new Vector3(Mathf.Cos(next),0,Mathf.Sin(next));
                Tri(foamVertices,foamTriangles,center+a*radius,center+b*(radius+.10f),center+b*radius);
                Tri(foamVertices,foamTriangles,center+a*radius,center+a*(radius+.10f),center+b*(radius+.10f));
            }
        }
        // 几块近景主石建立轮廓，后面的石群更小，保留河道连续的水面。
        Add("WaterHeroNear",RiverX(5)+2,5,5.3f,true,0);
        Add("WaterHeroMiddle",RiverX(23)-3,23,3.8f,true,2);
        Add("WaterHeroFar",RiverX(47)+3,47,4.1f,true,0);
        for(int cluster=0;cluster<18;cluster++)
        {
            float z=R(-8,145),x=RiverX(z)+R(-.65f,.65f)*Width(z);
            Add("WaterBoulder_"+cluster,x,z,R(1.7f,3.5f),true,cluster%3);
            for(int j=0;j<3;j++)Add("WaterSatellite_"+cluster+"_"+j,x+R(-2,2),z+R(-2,2),R(.6f,1.8f),true,0);
        }
        for(int cluster=0;cluster<45;cluster++)
        {
            float x=R(-46,45),z=R(-18,85);
            if(!GrassAllowed(x,z)||Vector2.Distance(new Vector2(x,z),new Vector2(Camp.x,Camp.z))<7)continue;
            Add("LandBoulder_"+cluster,x,z,R(1.2f,3.2f),false,cluster%3);
            for(int j=0;j<4;j++)
            {float px=x+R(-1.7f,1.7f),pz=z+R(-1.7f,1.7f);if(GrassAllowed(px,pz))Add("LandPebble_"+cluster+"_"+j,px,pz,R(.3f,.95f),false,j%3);}
        }
        Add("ForegroundLeftBoulder",-24,-3,3.4f,false,1);
        Add("ForegroundMiddleBoulder",-19,-8,2.5f,false,1);
        Add("ForegroundRightBoulder",-14,-8,2.2f,false,2);
        MeshGo(root,"BoulderWaterRims",Mesh("BoulderWaterRims",foamVertices,foamTriangles),Mat("BoulderFoam",new Color(.72f,.83f,.78f),true)).GetComponent<MeshRenderer>().shadowCastingMode=ShadowCastingMode.Off;
    }

    /// <summary>幂等替换补充石景，保持已有植被、地形与相机构图；重拍后关闭由本次打开的样片。</summary>
    public static string UpdateRocks()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling)throw new InvalidOperationException("需要空闲编辑模式");
        var previous=SceneManager.GetActiveScene();var scene=SceneManager.GetSceneByPath(ScenePath);bool opened=!scene.isLoaded;
        if(opened)scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
        if(scene.isDirty)throw new InvalidOperationException("样片有未保存修改，停止石景重做");SceneManager.SetActiveScene(scene);
        try
        {
            Random=new System.Random(61012);var root=scene.GetRootGameObjects().Single(g=>g.name=="ValleyArtSample").transform;
            var old=root.Find("SupplementalRockGroups");if(old!=null)Object.DestroyImmediate(old.gameObject);
            var rocks=new GameObject("SupplementalRockGroups");rocks.transform.SetParent(root,false);DressingRocks(rocks.transform);
            AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene,ScenePath);
            var camera=root.GetComponentInChildren<Camera>();Capture(camera,"camp-landscape");Capture(camera,"camp-rocks");
            var position=camera.transform.position;var rotation=camera.transform.rotation;
            camera.transform.position=Camp+new Vector3(11,12,-16);camera.transform.LookAt(Camp+new Vector3(2,0,2));Capture(camera,"gameplay-angle");
            camera.transform.position=position;camera.transform.rotation=rotation;EditorSceneManager.SaveScene(scene,ScenePath);
            var report=Newtonsoft.Json.JsonConvert.SerializeObject(new{waterRocks=rocks.GetComponentsInChildren<Transform>().Count(t=>t.name.StartsWith("Water")),landRocks=rocks.GetComponentsInChildren<Transform>().Count(t=>t.name.StartsWith("Land")||t.name.StartsWith("Foreground")),backupDependencies=AssetDatabase.GetDependencies(ScenePath,true).Where(p=>p.StartsWith("Assets/Art/")).ToArray()},Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText(Output+"/rocks-verification.json",report);return report;
        }
        finally{if(opened)EditorSceneManager.CloseScene(scene,true);if(previous.IsValid()&&previous.isLoaded)SceneManager.SetActiveScene(previous);}
    }
    /// <summary>只更新已保存样片的草丛，保持山体、树林、相机及营地构图；拍摄完成后恢复原编辑器场景。</summary>
    public static string UpdateGrass()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling)throw new InvalidOperationException("需要空闲编辑模式");
        var previous=SceneManager.GetActiveScene();var scene=SceneManager.GetSceneByPath(ScenePath);bool opened=!scene.isLoaded;
        if(opened)scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
        if(scene.isDirty)throw new InvalidOperationException("样片有未保存修改，停止草丛重做");
        SceneManager.SetActiveScene(scene);
        try
        {
            Random=new System.Random(61011);var root=scene.GetRootGameObjects().Single(g=>g.name=="ValleyArtSample").transform;
            var old=root.Find("MeadowTufts");if(old!=null)Object.DestroyImmediate(old.gameObject);
            var cover=new GameObject("MeadowTufts");cover.transform.SetParent(root,false);Meadow(cover.transform);
            AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene,ScenePath);
            var camera=root.GetComponentInChildren<Camera>();Capture(camera,"camp-landscape");
            var position=camera.transform.position;var rotation=camera.transform.rotation;
            camera.transform.position=Camp+new Vector3(11,12,-16);camera.transform.LookAt(Camp+new Vector3(2,0,2));Capture(camera,"gameplay-angle");
            camera.transform.position=position;camera.transform.rotation=rotation;
            EditorSceneManager.SaveScene(scene,ScenePath);
            var report=Newtonsoft.Json.JsonConvert.SerializeObject(new{grassRenderers=cover.GetComponentsInChildren<Renderer>().Length,grassColliders=cover.GetComponentsInChildren<Collider>().Length,grassTriangles=cover.GetComponentsInChildren<MeshFilter>().Sum(m=>m.sharedMesh.triangles.Length/3),backupDependencies=AssetDatabase.GetDependencies(ScenePath,true).Where(p=>p.StartsWith("Assets/Art/")).ToArray()},Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText(Output+"/grass-verification.json",report);return report;
        }
        finally{if(opened)EditorSceneManager.CloseScene(scene,true);if(previous.IsValid()&&previous.isLoaded)SceneManager.SetActiveScene(previous);}
    }

    /// <summary>创建附加场景并保存样片；保留已打开场景，失败关闭临时场景，不写正式地图或全局玩家状态。</summary>
    public static string Run()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling)throw new InvalidOperationException("需要空闲编辑模式");
        if(SceneManager.GetSceneByPath(ScenePath).isLoaded)throw new InvalidOperationException("样片已经打开，请先关闭后再重做");
        Random=new System.Random(61010);
        Folder(Art);Directory.CreateDirectory(Output);var previous=SceneManager.GetActiveScene();
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);SceneManager.SetActiveScene(scene);
        try
        {
            var root=new GameObject("ValleyArtSample");var land=new GameObject("LandAndRiver");land.transform.SetParent(root.transform,false);
            var greens=new[]{Mat("Meadow",new Color(.28f,.36f,.14f)),Mat("MeadowLight",new Color(.31f,.38f,.15f)),Mat("MeadowShade",new Color(.26f,.34f,.14f)),Mat("MeadowSoft",new Color(.29f,.35f,.14f)),Mat("BankSand",new Color(.55f,.48f,.34f))};
            Ground(land.transform,greens);
            var water=Save(new Material(AssetDatabase.LoadAssetAtPath<Material>(Nature+"River/RiverWater.mat")),"SampleWater.mat");
            water.SetColor("_Color",new Color(.30f,.66f,.71f,.88f));water.SetColor("_DepthColor",new Color(.08f,.43f,.52f,.88f));
            water.SetFloat("_MaxDepth",3);water.SetFloat("_RippleNoiseScale",7);water.SetFloat("_RippleHeight",.035f);EditorUtility.SetDirty(water);
            River(land.transform,water,Mat("WaterGlint",new Color(.69f,.85f,.81f),true));
            var ridgeRoot=new GameObject("MountainLayers");ridgeRoot.transform.SetParent(root.transform,false);
            var distant=Mat("DistantSlate",new Color(.36f,.47f,.61f));var snow=Mat("MountainPale",new Color(.71f,.78f,.87f));
            for(int i=0;i<9;i++)Mountain(ridgeRoot.transform,i,new Vector3(-600+i*150,5,550+R(-40,80)),R(135,220),R(100,230),distant,snow);
            var clouds=new GameObject("AngularClouds");clouds.transform.SetParent(root.transform,false);Clouds(clouds.transform);
            var trees=new GameObject("ForestClusters");trees.transform.SetParent(root.transform,false);
            var sources=Enumerable.Range(1,5).Select(i=>AssetDatabase.LoadAssetAtPath<GameObject>(Nature+"Vegetation/Tree_0"+i+".prefab")).ToArray();
            // 导出需要的松树网格，复用已接入的RPG色板材质，样片不直接依赖备用原包。
            var pineSource=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/RPGPP_LT/Prefabs/Nature/Vegetation/Trees/rpgpp_lt_tree_pine_01.prefab");
            var pineRoot=new GameObject("PineVisual");var pineMesh=Save(Object.Instantiate(pineSource.GetComponentInChildren<MeshFilter>().sharedMesh),"PineVisual.asset");
            pineRoot.AddComponent<MeshFilter>().sharedMesh=pineMesh;pineRoot.AddComponent<MeshRenderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(Nature+"Sources/RPGPP_LT/Materials/rpgpp_lt_mat_a.mat");
            var pine=PrefabUtility.SaveAsPrefabAsset(pineRoot,Art+"/PineVisual.prefab");Object.DestroyImmediate(pineRoot);
            var rock=AssetDatabase.LoadAssetAtPath<GameObject>(Nature+"Sources/SimpleNaturePack/Prefabs/Rock_01.prefab");
            var stones=new GameObject("RiverbankRocks");stones.transform.SetParent(root.transform,false);
            var stoneMat=Mat("RiverStone",new Color(.44f,.45f,.39f));
            for(int i=0;i<650;i++)
            {
                float x=R(-125,125),z=R(-35,375);var d=Mathf.Abs(x-RiverX(z));
                if(d<Width(z)+7||Vector2.Distance(new Vector2(x,z),new Vector2(Camp.x,Camp.z))<12)continue;
                if(Mathf.PerlinNoise(x*.034f+19,z*.025f+5)<.42f)continue;
                Place(trees.transform,i%3==0?sources[i%5]:pine,"TreeCluster_"+i,new Vector3(x,Height(x,z),z),i%3==0?R(1.3f,2.5f):R(.9f,1.8f));
            }
            // 近景树林框住营地与河道，留出营地入口和主要视觉走廊。
            Place(trees.transform,sources[2],"CampCanopy",new Vector3(-27,Height(-27,4),4),2.7f);
            Place(trees.transform,pine,"CampPine",new Vector3(-31,Height(-31,17),17),1.9f);
            Place(trees.transform,pine,"CampPineNear",new Vector3(-12,Height(-12,19),19),1.5f);
            for(int i=0;i<130;i++)
            {
                float z=R(-40,330),side=i%2==0?-1:1,x=RiverX(z)+side*(Width(z)+R(-1.5f,5));
                var p=new Vector3(x,Height(x,z)-.15f,z);var g=Place(stones.transform,rock,"BankBoulder_"+i,p,R(.8f,4.5f));
                foreach(var r in g.GetComponentsInChildren<MeshRenderer>(true))r.sharedMaterial=stoneMat;
            }
            for(int i=0;i<90;i++)
            {
                float z=R(-12,65),x=RiverX(z)+(i%2==0?-1:1)*(Width(z)+R(1,5));
                var g=Place(stones.transform,rock,"ShorePebble_"+i,new Vector3(x,Height(x,z)-.06f,z),R(.3f,.75f));
                foreach(var r in g.GetComponentsInChildren<MeshRenderer>(true))r.sharedMaterial=stoneMat;
            }
            var cover=new GameObject("MeadowTufts");cover.transform.SetParent(root.transform,false);
            Meadow(cover.transform);
            var supplemental=new GameObject("SupplementalRockGroups");supplemental.transform.SetParent(root.transform,false);DressingRocks(supplemental.transform);
            var camp=new GameObject("RiversideCamp");camp.transform.SetParent(root.transform,false);
            var hide=Mat("WeatheredHide",new Color(.54f,.40f,.25f));var wood=Mat("Branches",new Color(.28f,.18f,.09f));
            Shelter(camp.transform,hide,wood);Fire(camp.transform,rock,wood,Mat("FireAmber",new Color(1,.52f,.12f),true));
            var sun=new GameObject("WarmAfternoonSun",typeof(Light));sun.transform.SetParent(root.transform,false);sun.transform.rotation=Quaternion.Euler(32,-48,0);
            var light=sun.GetComponent<Light>();light.type=LightType.Directional;light.intensity=1.25f;light.color=new Color(1,.90f,.72f);light.shadows=LightShadows.Soft;
            RenderSettings.sun=light;RenderSettings.ambientMode=AmbientMode.Trilight;RenderSettings.ambientSkyColor=new Color(.52f,.65f,.76f);RenderSettings.ambientEquatorColor=new Color(.51f,.53f,.39f);RenderSettings.ambientGroundColor=new Color(.24f,.26f,.20f);
            RenderSettings.fog=true;RenderSettings.fogMode=FogMode.Linear;RenderSettings.fogStartDistance=160;RenderSettings.fogEndDistance=1100;RenderSettings.fogColor=new Color(.61f,.71f,.76f);
            var sky=new Material(Shader.Find("Skybox/Procedural"));sky.SetFloat("_SunSize",.035f);sky.SetFloat("_AtmosphereThickness",1.3f);sky.SetFloat("_Exposure",1.2f);sky.SetColor("_SkyTint",new Color(.60f,.65f,.70f));sky.SetColor("_GroundColor",new Color(.48f,.50f,.43f));RenderSettings.skybox=Save(sky,"SampleSky.mat");
            var cameraGo=new GameObject("SampleCamera",typeof(Camera));cameraGo.transform.SetParent(root.transform,false);cameraGo.tag="MainCamera";
            var camera=cameraGo.GetComponent<Camera>();camera.scene=scene;camera.clearFlags=CameraClearFlags.Skybox;camera.nearClipPlane=.1f;camera.farClipPlane=1800;camera.fieldOfView=63;
            cameraGo.transform.position=new Vector3(-24,5.1f,-15);cameraGo.transform.LookAt(new Vector3(25,6,95));
            var data=cameraGo.AddComponent<UniversalAdditionalCameraData>();data.requiresDepthTexture=true;data.requiresColorTexture=true;
            var volumeGo=new GameObject("SampleColorGrade",typeof(Volume));volumeGo.transform.SetParent(root.transform,false);
            var profile=ScriptableObject.CreateInstance<VolumeProfile>();var tone=profile.Add<Tonemapping>();tone.mode.Override(TonemappingMode.ACES);
            var color=profile.Add<ColorAdjustments>();color.saturation.Override(-7);color.contrast.Override(5);
            var profilePath=Art+"/SampleColorGrade.asset";var oldProfile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
            if(oldProfile!=null)
            {
                foreach(var component in oldProfile.components.ToArray())Object.DestroyImmediate(component,true);
                oldProfile.components.Clear();foreach(var component in profile.components){oldProfile.components.Add(component);AssetDatabase.AddObjectToAsset(component,oldProfile);}EditorUtility.SetDirty(oldProfile);Object.DestroyImmediate(profile);
            }
            else {AssetDatabase.CreateAsset(profile,profilePath);foreach(var component in profile.components)AssetDatabase.AddObjectToAsset(component,profile);oldProfile=profile;}
            volumeGo.GetComponent<Volume>().isGlobal=true;volumeGo.GetComponent<Volume>().sharedProfile=oldProfile;data.renderPostProcessing=true;
            AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene,ScenePath);
            Capture(camera,"camp-landscape");
            var pose=cameraGo.transform.position;var rotation=cameraGo.transform.rotation;
            cameraGo.transform.position=Camp+new Vector3(11,12,-16);cameraGo.transform.LookAt(Camp+new Vector3(2,0,2));Capture(camera,"gameplay-angle");
            cameraGo.transform.position=new Vector3(80,150,-85);cameraGo.transform.LookAt(new Vector3(0,0,80));Capture(camera,"layout-overview");
            cameraGo.transform.position=pose;cameraGo.transform.rotation=rotation;
            File.WriteAllText(Output+"/audit.json",Newtonsoft.Json.JsonConvert.SerializeObject(new{scene=ScenePath,renderers=root.GetComponentsInChildren<Renderer>().Length,trees=trees.transform.childCount,rocks=stones.transform.childCount,groundCover=cover.transform.childCount,colliders=root.GetComponentsInChildren<Collider>().Length,monoBehaviours=root.GetComponentsInChildren<MonoBehaviour>().Select(m=>m.GetType().Name).Distinct().ToArray(),dependencies=AssetDatabase.GetDependencies(ScenePath,true).Where(p=>p.StartsWith("Assets/Art/")).ToArray()},Newtonsoft.Json.Formatting.Indented));
            // 预览拍摄改变相机后恢复并重新保存，确保打开场景时看到交付构图。
            EditorSceneManager.SaveScene(scene,ScenePath);return "Saved "+ScenePath+"; captures: "+Output;
        }
        catch{EditorSceneManager.CloseScene(scene,true);if(previous.IsValid()&&previous.isLoaded)SceneManager.SetActiveScene(previous);throw;}
    }
    public static void Capture(Camera camera,string name)
    {
        Directory.CreateDirectory(Output);var rt=new RenderTexture(1600,900,24);rt.Create();var image=new Texture2D(1600,900,TextureFormat.RGB24,false);var old=RenderTexture.active;
        try{RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1600,900),0,0);image.Apply();File.WriteAllBytes(Output+"/"+name+".png",image.EncodeToPNG());}
        finally{RenderTexture.active=old;Object.DestroyImmediate(image);rt.Release();Object.DestroyImmediate(rt);}
    }
}
