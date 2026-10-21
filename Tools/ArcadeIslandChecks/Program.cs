using FirepowerLoop.Arcade;
// 范围：ArcadeIslandGeometryChecks，仅 4 项几何检查，逐项输出通过结果。
var opts=args.Select(a=>a.Split('=',2)).ToDictionary(a=>a[0],a=>a.Length>1?a[1]:"");
if(opts.GetValueOrDefault("testClass")!="ArcadeIslandGeometryChecks"||opts.GetValueOrDefault("includePassingTests")!="true"||
    !int.TryParse(opts.GetValueOrDefault("maxTests"),out int max)||max<4)
    throw new ArgumentException("Explicit ArcadeIslandGeometryChecks scope of four required");
void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
var constructor=typeof(ArcadeIslandRegion).GetConstructor(new[]{typeof(float),typeof(float),typeof(int)});
Require(constructor!=null,"四图轮廓构造合同缺失");
var regions=Enumerable.Range(1,4).Select(round=>(ArcadeIslandRegion)constructor!.Invoke(new object[]{19.5f,19.5f,round})).ToArray();
Require(regions.Select(r=>string.Join(";",r.Outer.Select(p=>$"{p.X:R},{p.Y:R}"))).Distinct().Count()==4,"四图外缘必须不同");
Console.WriteLine("PASS Four distinct fixed outlines");
foreach(var r in regions){Require(r.Contains(0,0)&&!r.IsShallow(0,0),"出生陆地不安全");foreach(var edge in r.Outer){var p=r.Constrain(edge.X,edge.Y,.55f);Require(r.Contains(p.X,p.Y),"边界约束越界");}}
Console.WriteLine("PASS Safe land spawn and constrained boundaries");
Require(regions[2].IsShallow(-18,0)&&!regions[2].IsShallow(11,0),"第三图需西侧海岸与东侧干燥通路");
Console.WriteLine("PASS West coast and dry east passage");
var legacy=new ArcadeIslandRegion(19.5f,19.5f);Require(legacy.Outer.Count==96&&legacy.Contains(0,0),"旧双端合同失效");
Console.WriteLine("PASS Legacy default remains available");
