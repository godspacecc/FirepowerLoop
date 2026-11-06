using System;using Object=UnityEngine.Object;using UnityEngine;using UnityEditor;using UnityEditor.SceneManagement;using UnityEngine.SceneManagement;using System.IO;using System.Linq;using System.Collections.Generic;using System.Globalization;
/// <summary>采用版幸存者农场制作入口；只保存同源合并资源及Config权威布局，保留既有meta。</summary>
public static class SurvivorFarmBake{

/// <summary>逐级创建正式目录，重复制作不删除已有文件或meta。</summary>
static void Folder(string path){if(AssetDatabase.IsValidFolder(path))return;var parent=path.Substring(0,path.LastIndexOf('/'));Folder(parent);AssetDatabase.CreateFolder(parent,path.Substring(path.LastIndexOf('/')+1));}
/// <summary>保留隔簇草叶并压紧顶点，保持每簇三片双面草的完整形状。</summary>
static Mesh ReduceGrass(Mesh original){
var v=original.vertices;var n=original.normals;var indices=original.triangles;
if(indices.Length%18!=0)throw new InvalidOperationException("草叶簇拓扑不完整");
var vertices=new List<Vector3>();var normals=new List<Vector3>();var triangles=new List<int>();var map=new Dictionary<int,int>();
for(int cluster=0;cluster<indices.Length/18;cluster+=2)for(int j=0;j<18;j++){int old=indices[cluster*18+j];int next;if(!map.TryGetValue(old,out next)){next=vertices.Count;map.Add(old,next);vertices.Add(v[old]);normals.Add(n[old]);}triangles.Add(next);}
var result=new Mesh();result.SetVertices(vertices);result.SetNormals(normals);result.SetTriangles(triangles,0);result.RecalculateBounds();return result;
}
static T Save<T>(T candidate,string path)where T:Object{var existing=AssetDatabase.LoadAssetAtPath<T>(path);if(existing==null){AssetDatabase.CreateAsset(candidate,path);owned.Remove(candidate);return candidate;}if(existing is Material material){if(candidate.name=="GrassBlades"||candidate.name=="AccessibleBarnRoofTransparent"){material.color=((Material)(Object)candidate).color;EditorUtility.SetDirty(material);AssetDatabase.SaveAssetIfDirty(material);}return existing;}EditorUtility.CopySerialized(candidate,existing);EditorUtility.SetDirty(existing);AssetDatabase.SaveAssetIfDirty(existing);return existing;}
static Bounds Union(IEnumerable<Renderer> renderers){bool first=true;var result=new Bounds();foreach(var r in renderers){if(first){result=r.bounds;first=false;}else result.Encapsulate(r.bounds);}return result;}
/// <summary>从真实可见实体整理二维足迹，裁去玩家边界之外的部分。</summary>
static List<(string name,Bounds bounds)> Footprints(){var result=new List<(string,Bounds)>();
foreach(Transform t in root)if(t.name.StartsWith("StoneWall_",StringComparison.Ordinal))result.Add((t.name,Union(t.GetComponentsInChildren<Renderer>())));
foreach(var group in root.GetComponentsInChildren<MeshRenderer>().Where(r=>r.name=="FencePost"||r.name=="FenceRail").GroupBy(r=>Mathf.RoundToInt(r.transform.position.z)).OrderBy(g=>g.Key))result.Add(("Fence_"+group.Key,Union(group)));
foreach(var wall in root.GetComponentsInChildren<MeshRenderer>().Where(r=>r.name.StartsWith("PassableBarnWall",StringComparison.Ordinal)))result.Add((wall.name,wall.bounds));
foreach(var r in root.GetComponentsInChildren<MeshRenderer>().Where(r=>r.name=="RoundHayBale"||r.name=="TreeTrunk"))result.Add((r.name,r.bounds));
var clipped=new List<(string,Bounds)>();foreach(var part in result){var b=part.Item2;float minX=Mathf.Max(-30.48f,b.min.x),maxX=Mathf.Min(30.48f,b.max.x),minZ=Mathf.Max(-30.48f,b.min.z),maxZ=Mathf.Min(30.48f,b.max.z);if(maxX-minX<.001f||maxZ-minZ<.001f)continue;clipped.Add((part.Item1,new Bounds(new Vector3((minX+maxX)/2,0,(minZ+maxZ)/2),new Vector3(maxX-minX,1,maxZ-minZ))));}return clipped;}
/// <summary>固定材质合并绘制组；正式环境没有业务组件、碰撞或摄像机。</summary>
static object SaveConfirmed(Scene scene){
const string art="Assets/Game/Art/Survivor/Farm";Folder(art);var footprints=Footprints();var sourceRenderers=root.GetComponentsInChildren<Renderer>();
var camera=new GameObject("TemporaryBakeCamera").AddComponent<Camera>();SceneManager.MoveGameObjectToScene(camera.gameObject,scene);camera.transform.position=new Vector3(-9,9,-31);camera.transform.LookAt(new Vector3(0,2,16));
var groups=new Dictionary<Material,List<CombineInstance>>();
foreach(var r in sourceRenderers){Mesh mesh;var filter=r.GetComponent<MeshFilter>();if(filter!=null)mesh=filter.sharedMesh;else if(r is LineRenderer line){mesh=new Mesh();line.BakeMesh(mesh,camera,true);owned.Add(mesh);}else throw new InvalidOperationException("不支持的环境Renderer："+r.name);
if(mesh==null)throw new InvalidOperationException("环境缺少Mesh："+r.name);var materials=r.sharedMaterials;for(int sub=0;sub<mesh.subMeshCount;sub++){var mat=materials[Mathf.Min(sub,materials.Length-1)];if(!groups.TryGetValue(mat,out var list)){list=new List<CombineInstance>();groups.Add(mat,list);}list.Add(new CombineInstance{mesh=mesh,subMeshIndex=sub,transform=root.worldToLocalMatrix*r.transform.localToWorldMatrix});}}
var output=new GameObject("SurvivorFarmVisual");SceneManager.MoveGameObjectToScene(output,scene);int index=0;long vertices=0;
foreach(var pair in groups.OrderBy(x=>x.Key.name,StringComparer.Ordinal)){var mat=Save(pair.Key,art+"/"+pair.Key.name+".mat");var mesh=new Mesh{name="SurvivorFarm_"+pair.Key.name,indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};owned.Add(mesh);mesh.CombineMeshes(pair.Value.ToArray(),true,true);mesh.RecalculateBounds();vertices+=mesh.vertexCount;var saved=Save(mesh,art+"/"+mesh.name+".asset");var part=new GameObject("Part_"+(index++)+"_"+mat.name,typeof(MeshFilter),typeof(MeshRenderer));part.transform.SetParent(output.transform,false);part.GetComponent<MeshFilter>().sharedMesh=saved;part.GetComponent<MeshRenderer>().sharedMaterial=mat;if(mat.name.StartsWith("AccessibleBarnTimber",StringComparison.Ordinal)){
 string suffix=mat.name.EndsWith("West",StringComparison.Ordinal)?"West":"";
 part.name="AccessibleBarnRoof"+suffix;
 foreach(string planeName in new[]{"RoofPlaneLeft","RoofPlaneRight"}){
 var source=root.Find(planeName+suffix);var plane=new GameObject(planeName).transform;plane.SetParent(part.transform,false);
 plane.localPosition=source.localPosition;plane.localRotation=source.localRotation;plane.localScale=source.localScale;
 }
 var faded=Mat("AccessibleBarnRoofTransparent",new Color(mat.color.r,mat.color.g,mat.color.b,.32f));
 faded.SetFloat("_Surface",1);faded.SetFloat("_SrcBlend",(float)UnityEngine.Rendering.BlendMode.SrcAlpha);
 faded.SetFloat("_DstBlend",(float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
 faded.SetFloat("_ZWrite",0);faded.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");faded.SetOverrideTag("RenderType","Transparent");
 faded.renderQueue=(int)UnityEngine.Rendering.RenderQueue.Transparent;
 part.AddComponent<Game.SurvivorBarnRoof>().TransparentMaterial=Save(faded,art+"/"+faded.name+".mat");
}
if(mat.name=="GrassBlades")part.GetComponent<MeshRenderer>().shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;}
var markers=new GameObject("BlockingFootprints");markers.transform.SetParent(output.transform,false);var lines=new List<string>{"name|x|z|halfWidth|halfHeight"};string F(float f)=>f.ToString("0.######",CultureInfo.InvariantCulture);
for(int i=0;i<footprints.Count;i++){var b=footprints[i].bounds;var marker=new GameObject(i.ToString("D2")+"_"+footprints[i].name);marker.transform.SetParent(markers.transform,false);marker.transform.localPosition=b.center;marker.transform.localScale=b.size;lines.Add(footprints[i].name+"|"+F(b.center.x)+"|"+F(b.center.z)+"|"+F(b.extents.x)+"|"+F(b.extents.z));}
PrefabUtility.SaveAsPrefabAsset(output,"Assets/Game/Prefabs/SceneObjects/SurvivorFarmVisual.prefab");
var repo=Path.GetFullPath(Path.Combine(Application.dataPath,"../.."));var rows=File.ReadAllLines(Path.Combine(repo,"Config/DataCsv/survivor_map_layout.csv"));var header=rows.First(r=>r.StartsWith("##var,")).Split(',');var row=rows.Single(r=>r.StartsWith(",arena.22.round.1,")).Split(',');
void Cell(string name,string value){int at=Array.IndexOf(header,name);if(at<0)throw new InvalidOperationException("布局源字段缺失："+name);row[at]=value;}
Cell("Obstacles",string.Join(";",footprints.Select(p=>F(p.bounds.center.x)+"|"+F(p.bounds.center.z)+"|"+F(p.bounds.extents.x)+"|"+F(p.bounds.extents.z))));Cell("MovementOnlyObstacleIndices","");Cell("WeaponSpawnPoints","");Cell("WeaponSpawnCount","0");
File.WriteAllLines(Path.Combine(repo,"Config/DataCsv/survivor_map_layout.csv"),rows.Where(r=>r.StartsWith("##")).Concat(new[]{string.Join(",",row)}),new System.Text.UTF8Encoding(false));
string batch=Path.Combine(repo,"Backup/Artifacts/SurvivorSecondBarn-20261106/applied");Directory.CreateDirectory(batch);File.WriteAllLines(Path.Combine(batch,"baked-footprints.tsv"),lines);File.WriteAllText(Path.Combine(batch,"bake-summary.txt"),"Render groups="+index+"; vertices="+vertices+"; footprints="+footprints.Count+"; source renderers="+sourceRenderers.Length);return new{renderGroups=index,vertices,footprints=footprints.Count};
}

/// <summary>制作期间拥有的临时材质和网格，保存为正式资产后移交AssetDatabase。</summary>
static List<Object> owned=new List<Object>();static Transform root;static System.Random rng=new System.Random(72);
/// <summary>创建采用版纯色材质，按名称持久化复用。</summary>
static Material Mat(string name,Color c){var m=new Material(Shader.Find("Universal Render Pipeline/Lit"));m.name=name;m.color=c;m.SetFloat("_Smoothness",.05f);owned.Add(m);return m;}
/// <summary>创建临时方形几何，碰撞体不进入正式合并结果。</summary>
static GameObject Cube(string name,Vector3 p,Vector3 scale,Material m){var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=name;g.transform.SetParent(root);g.transform.position=p;g.transform.localScale=scale;g.GetComponent<Renderer>().sharedMaterial=m;return g;}
/// <summary>采用版树冠和散石共用二十面体单元。</summary>
static Mesh StoneMesh(){float t=(1+Mathf.Sqrt(5))/2;var vs=new[]{new Vector3(-1,t,0),new Vector3(1,t,0),new Vector3(-1,-t,0),new Vector3(1,-t,0),new Vector3(0,-1,t),new Vector3(0,1,t),new Vector3(0,-1,-t),new Vector3(0,1,-t),new Vector3(t,0,-1),new Vector3(t,0,1),new Vector3(-t,0,-1),new Vector3(-t,0,1)};int[] ts={0,11,5,0,5,1,0,1,7,0,7,10,0,10,11,1,5,9,5,11,4,11,10,2,10,7,6,7,1,8,3,9,4,3,4,2,3,2,6,3,6,8,3,8,9,4,9,5,2,4,11,6,2,10,8,6,7,9,8,1};var v=new Vector3[ts.Length];for(int i=0;i<v.Length;i++)v[i]=vs[ts[i]].normalized;var mesh=new Mesh();mesh.vertices=v;mesh.triangles=Enumerable.Range(0,v.Length).ToArray();mesh.RecalculateNormals();mesh.RecalculateBounds();owned.Add(mesh);return mesh;}
/// <summary>用共享单元组装低多边形物件。</summary>
static GameObject Lump(string name,Mesh mesh,Vector3 p,Vector3 scale,Material m){var g=new GameObject(name);g.transform.SetParent(root);g.transform.position=p;g.transform.localScale=scale;g.AddComponent<MeshFilter>().sharedMesh=mesh;g.AddComponent<MeshRenderer>().sharedMaterial=m;return g;}
/// <summary>错层石墙同时保留组身份，供真实几何足迹提取。</summary>
static void Wall(Mesh mesh,float start,float end,float z,Material[] stones){var group=new GameObject("StoneWall_"+start+"_"+end+"_"+z);group.transform.SetParent(root,false);for(int row=0;row<3;row++)for(float x=start+row*.32f;x<end;x+=1.12f){if(row==2&&(x>end-1.5f||rng.NextDouble()<.16))continue;var g=Cube("RoughStoneWall",new Vector3(x,.22f+row*.4f,z+(float)rng.NextDouble()*.12f),new Vector3(1.02f+(float)rng.NextDouble()*.10f,.43f,.66f),stones[rng.Next(stones.Length)]);g.transform.rotation=Quaternion.Euler((float)rng.NextDouble()*5,(float)rng.NextDouble()*9-4,(float)rng.NextDouble()*5-2);g.transform.SetParent(group.transform,true);}}
/// <summary>保留采用版木栅柱和双横杆比例。</summary>
static void Fence(float x,float z,int count,Material wood){for(int i=0;i<=count;i++)Cube("FencePost",new Vector3(x+i*2.4f,.8f,z),new Vector3(.25f,1.6f,.25f),wood);for(int i=0;i<count;i++)foreach(float y in new[]{.6f,1.15f})Cube("FenceRail",new Vector3(x+i*2.4f+1.2f,y,z),new Vector3(2.5f,.18f,.17f),wood);}
/// <summary>重制已采用几何，限定本工程空闲编辑态，不构建任何资源包。</summary>
public static object Bake(){if(EditorApplication.isPlaying||EditorApplication.isCompiling||!Application.dataPath.Replace((char)92,(char)47).EndsWith("/FirepowerLoop/Client/Assets",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("需本工程空闲编辑态");rng=new System.Random(72);var scene=EditorSceneManager.NewPreviewScene();var holder=new GameObject("LoadingArtworkFarmCandidate");root=holder.transform;SceneManager.MoveGameObjectToScene(holder,scene);try{
var mesh=StoneMesh();var grass=Mat("MutedGrass",new Color(.42f,.48f,.22f));var dirt=Mat("WarmEarth",new Color(.47f,.37f,.20f));var wood=Mat("AgedTimber",new Color(.35f,.24f,.13f));var hay=Mat("GoldenHay",new Color(.72f,.56f,.22f));var cream=Mat("FarmPlaster",new Color(.64f,.60f,.43f));var foliage=Mat("OliveFoliage",new Color(.30f,.39f,.16f));var foliage2=Mat("OliveFoliageLight",new Color(.43f,.49f,.22f));var stone=new[]{Mat("StoneA",new Color(.40f,.42f,.36f)),Mat("StoneB",new Color(.48f,.49f,.42f)),Mat("StoneC",new Color(.34f,.38f,.34f))};
Cube("WalkableFarmGround",new Vector3(0,-1.01f,0),new Vector3(70,2,70),grass);
var fieldMesh=new Mesh();var fv=new List<Vector3>();var ft=new[]{new List<int>(),new List<int>(),new List<int>()};
for(int z=0;z<51;z++)for(int x=0;x<51;x++){float xx=Mathf.Clamp(-100+x*4,-54,54),zz=Mathf.Clamp(-70+z*4,-54,54);float outer=Mathf.Max(0,Mathf.Max(Mathf.Abs(xx)-40,zz-40));float y=outer==0?-.015f:Mathf.PerlinNoise(xx*.035f+4,zz*.035f+9)*Mathf.Min(outer*.2f,7);fv.Add(new Vector3(xx,y,zz));}
for(int z=0;z<50;z++)for(int x=0;x<50;x++){int at=z*51+x;float n=Mathf.PerlinNoise(x*.25f,z*.25f);int group=n<.36f?2:n>.6f?1:0;ft[group].AddRange(new[]{at,at+51,at+1,at+1,at+51,at+52});}
fieldMesh.SetVertices(fv);fieldMesh.subMeshCount=3;for(int i=0;i<3;i++)fieldMesh.SetTriangles(ft[i],i);fieldMesh.RecalculateNormals();fieldMesh.RecalculateBounds();owned.Add(fieldMesh);var field=new GameObject("TemporaryMeadow");field.transform.SetParent(root);field.AddComponent<MeshFilter>().sharedMesh=fieldMesh;field.AddComponent<MeshRenderer>().sharedMaterials=new[]{grass,Mat("PaleMeadow",new Color(.48f,.49f,.24f)),Mat("OliveMeadow",new Color(.38f,.43f,.20f))};
var tv=new List<Vector3>();var tt=new List<int>();for(int i=0;i<=70;i++){float x=-35+i,z=-4+Mathf.Sin(x*.12f)*2;tv.Add(new Vector3(x,.014f,z-1.45f));tv.Add(new Vector3(x,.014f,z+1.45f));if(i<70){int at=i*2;tt.AddRange(new[]{at,at+1,at+2,at+2,at+1,at+3});}}var track=new Mesh();track.SetVertices(tv);track.SetTriangles(tt,0);track.RecalculateNormals();owned.Add(track);var trackObject=new GameObject("WindingEarthTrack");trackObject.transform.SetParent(root);trackObject.AddComponent<MeshFilter>().sharedMesh=track;trackObject.AddComponent<MeshRenderer>().sharedMaterial=dirt;
Wall(mesh,-29,-15,5,stone);Wall(mesh,-9,5,5,stone);Wall(mesh,15,29,5,stone);Wall(mesh,-31,-22,-18,stone);Wall(mesh,21,30,-19,stone);
Fence(-26,-25,4,wood);Fence(15,-24,5,wood);Fence(-6,24,6,wood);
foreach(var barnCenter in new[]{new Vector3(20,0,21),new Vector3(-23,0,20)}){
string suffix=barnCenter.x<0?"West":"";
foreach(int side in new[]{-1,1}){
 Cube("PassableBarnWallSide"+side,barnCenter+new Vector3(side*4.3f,1.8f,0),new Vector3(.4f,3.6f,7),cream);
 foreach(int end in new[]{-1,1})Cube("PassableBarnWallEnd"+end+"_"+side,barnCenter+new Vector3(side*3,1.8f,end*3.3f),new Vector3(3,3.6f,.4f),cream);
}
var roofMaterial=Mat("AccessibleBarnTimber"+suffix,wood.color);
foreach(int side in new[]{-1,1}){
 var slope=Cube((side<0?"RoofPlaneLeft":"RoofPlaneRight")+suffix,barnCenter+new Vector3(side*2.35f,4.12f,0),new Vector3(4.98f,.16f,7.55f),roofMaterial);
 slope.transform.rotation=Quaternion.Euler(0,0,-side*18);
}
}
foreach(var p in new[]{new Vector3(-24,.8f,13),new Vector3(-21,.8f,13),new Vector3(22,.8f,-15),new Vector3(25,.8f,-15),new Vector3(-22,.8f,-24)}){var g=GameObject.CreatePrimitive(PrimitiveType.Cylinder);g.name="RoundHayBale";g.transform.SetParent(root);g.transform.position=p;g.transform.rotation=Quaternion.Euler(90,0,0);g.transform.localScale=new Vector3(2,1.4f,2);g.GetComponent<Renderer>().sharedMaterial=hay;}
var grovePoints=new[]{new Vector2(-39,18),new Vector2(-42,23),new Vector2(-38,27),new Vector2(-41,-17),new Vector2(-38,-22),new Vector2(39,14),new Vector2(42,18),new Vector2(39,24),new Vector2(40,-20),new Vector2(44,-24),new Vector2(-18,39),new Vector2(-12,42),new Vector2(-8,38),new Vector2(15,39),new Vector2(21,43),new Vector2(25,39)};
for(int i=0;i<grovePoints.Length;i++){float x=grovePoints[i].x,z=grovePoints[i].y;Cube("TreeTrunk",new Vector3(x,1.8f,z),new Vector3(.65f,3.6f,.65f),wood);Lump("TreeCrown",mesh,new Vector3(x,4.5f,z),new Vector3(2.4f,3,2.2f),i%2==0?foliage:foliage2);Lump("TreeCrownSide",mesh,new Vector3(x+.9f,3.8f,z-.5f),new Vector3(1.7f,1.9f,1.8f),foliage);
for(int b=0;b<5;b++){float a=b*Mathf.PI*2/5;Lump("LeafCluster",mesh,new Vector3(x+Mathf.Cos(a)*1.8f,4.7f+(b%2)*.8f,z+Mathf.Sin(a)*1.6f),new Vector3(1.5f,1.8f,1.5f),b%2==0?foliage:foliage2);}var branch=Cube("BranchFork",new Vector3(x+.45f,2.8f,z),new Vector3(.35f,2.2f,.35f),wood);branch.transform.rotation=Quaternion.Euler(0,0,-30);
}
root.Find("WalkableFarmGround").localScale=new Vector3(108,2,108);root.Find("WalkableFarmGround").position=new Vector3(0,-1.01f,0);
var grassDetail=Mat("GrassBlades",new Color(.40f,.46f,.23f));var verticesDetail=new List<Vector3>();var trianglesDetail=new List<int>();for(int k=0;k<7000;k++){float x=(float)rng.NextDouble()*66-33,z=(float)rng.NextDouble()*66-33;if(Mathf.Abs(z-(-4+Mathf.Sin(x*.12f)*2))<1.6f)continue;for(int b=0;b<3;b++){float a=(float)rng.NextDouble()*Mathf.PI*2;var c=new Vector3(x,.02f,z);var side=new Vector3(Mathf.Cos(a)*.035f,0,Mathf.Sin(a)*.035f);int start=verticesDetail.Count;verticesDetail.Add(c-side);verticesDetail.Add(c+side);verticesDetail.Add(c+new Vector3(.06f,.13f+(float)rng.NextDouble()*.16f,.03f));trianglesDetail.Add(start);trianglesDetail.Add(start+2);trianglesDetail.Add(start+1);trianglesDetail.Add(start);trianglesDetail.Add(start+1);trianglesDetail.Add(start+2);}}
var detailMesh=new Mesh();detailMesh.SetVertices(verticesDetail);detailMesh.SetTriangles(trianglesDetail,0);detailMesh.RecalculateNormals();detailMesh.RecalculateBounds();var fullGrass=detailMesh;detailMesh=ReduceGrass(fullGrass);Object.DestroyImmediate(fullGrass);owned.Add(detailMesh);var details=new GameObject("TemporaryGrassDetail");details.transform.SetParent(root);details.AddComponent<MeshFilter>().sharedMesh=detailMesh;details.AddComponent<MeshRenderer>().sharedMaterial=grassDetail;
for(int k=0;k<45;k++){float x=(float)rng.NextDouble()*58-29,z=(float)rng.NextDouble()*50-25;Lump("SmallFieldStone",mesh,new Vector3(x,.12f,z),new Vector3(.2f+(float)rng.NextDouble()*.4f,.18f,.24f),stone[k%3]);}
// Barn posts, roof slats, rolled hay seams and scattered stones are temporary preview geometry.
var dark=Mat("BarnInterior",new Color(.15f,.13f,.08f));var warmWood=Mat("WeatheredBoards",new Color(.43f,.30f,.17f));
foreach(var bale in root.GetComponentsInChildren<Transform>().Where(t=>t.name=="RoundHayBale").ToArray()){
for(int side=-1;side<=1;side+=2){var line=new GameObject("RolledStrawSpiral").AddComponent<LineRenderer>();line.transform.SetParent(root,false);line.sharedMaterial=warmWood;line.startWidth=line.endWidth=.025f;line.positionCount=91;line.useWorldSpace=true;for(int i=0;i<=90;i++){float t=i/90f,r=.08f+t*.82f,a=t*Mathf.PI*7;line.SetPosition(i,bale.position+new Vector3(Mathf.Cos(a)*r,Mathf.Sin(a)*r,side*1.42f));}}
}

var outerGrass=Mat("CandidateOuterGrass",new Color(.37f,.43f,.20f));
for(int side=0;side<4;side++)for(int i=0;i<45;i++){
float along=-48+(float)rng.NextDouble()*96,depth=33.8f+(float)rng.NextDouble()*15;
var p=Quaternion.Euler(0,side*90,0)*new Vector3(along,0,depth);
if(Mathf.Abs(p.x)<34 && Mathf.Abs(p.z)<34)continue;
float h=.35f+(float)rng.NextDouble()*.55f;
Lump("CandidateOuterShrub",mesh,p+Vector3.up*h*.5f,new Vector3(1.0f+(float)rng.NextDouble()*1.1f,h,.8f+(float)rng.NextDouble()),i%3==0?foliage2:outerGrass);
if(i%3==0)Lump("CandidateOuterStone",mesh,p+new Vector3(1.4f,.15f,.7f),new Vector3(.5f,.3f,.4f),stone[i%3]);
}
var edgeStone=Mat("CandidateBoundaryStone",new Color(.43f,.44f,.36f));
var edgeWood=Mat("CandidateBoundaryWood",new Color(.31f,.20f,.10f));
var edgeHedge=Mat("CandidateBoundaryHedge",new Color(.22f,.34f,.14f));
var edgeSoil=Mat("CandidateBoundarySoil",new Color(.41f,.35f,.22f));
var edgeMesh=StoneMesh();
// 临时围界内缘贴合31米场地外缘，主角中心按碰撞半径停止于内侧。
for(int side=0;side<4;side++){
 var turn=Quaternion.Euler(0,side*90,0);
 for(int section=0;section<8;section++){
  float start=-32+section*8;
  bool gate=(side==1&&section==4)||(side==3&&section==3);bool fence=gate||(section+side)%3==1;
  var strip=Cube("CandidateWornEdge",turn*new Vector3(start+4,.015f,30.2f),new Vector3(8,.035f,1.1f),edgeSoil);strip.transform.rotation=turn;
  for(int j=0;j<8;j++){
   float along=start+j+.5f;
   if(fence){
    var baseStone=Cube("CandidateFenceFoot",turn*new Vector3(along,.22f,31),new Vector3(1.01f,.44f,.7f),edgeStone);baseStone.transform.rotation=turn;
    if(j%2==0){var post=Cube("CandidateFencePost",turn*new Vector3(start+j,.8f,31),new Vector3(.23f,1.6f,.23f),edgeWood);post.transform.rotation=turn;}
   }else for(int row=0;row<3;row++){
    var boundaryBlock=Cube("CandidateBoundaryStone",turn*new Vector3(along,.20f+row*.4f,31),new Vector3(.97f,.39f,.72f),edgeStone);boundaryBlock.transform.rotation=turn*Quaternion.Euler(0,(j%3-1)*3,0);
   }
  }
  if(fence){
   if(gate){var brace=Cube("CandidateClosedFarmGate",turn*new Vector3(start+4,.92f,31),new Vector3(7.7f,.18f,.18f),edgeWood);brace.transform.rotation=turn*Quaternion.Euler(0,0,7);}
   foreach(float y in new[]{.65f,1.2f}){var rail=Cube("CandidateFenceRail",turn*new Vector3(start+4,y,31),new Vector3(8,.16f,.15f),edgeWood);rail.transform.rotation=turn;}
   if(!gate)for(int j=0;j<7;j++)Lump("CandidateDenseHedge",edgeMesh,turn*new Vector3(start+j*1.15f+.5f,.65f,32.2f),new Vector3(.9f,.8f,.8f),edgeHedge);
  }
 }
}

return SaveConfirmed(scene);
}finally{EditorSceneManager.ClosePreviewScene(scene);foreach(var o in owned)if(o!=null)Object.DestroyImmediate(o);owned.Clear();}}}