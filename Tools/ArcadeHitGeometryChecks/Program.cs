using System.Reflection;

// 具名定向检查；删除几何算法、改变最早命中或忽略高度都会产生失败。
internal static class Program
{
    // 限定最多十二项，始终输出通过项；不提供空筛选或全套回退。
    private static int Main(string[] args)
    {
        if (args.Length != 3 || args[0] != "testClass=ArcadeHitGeometryChecks" || args[1] != "maxTests=12" || args[2] != "includePassingTests=true") return 2;
        var assembly = Assembly.GetExecutingAssembly();
        var point = assembly.GetType("FirepowerLoop.Arcade.ArcadeHitPoint");
        var capsule = assembly.GetType("FirepowerLoop.Arcade.ArcadeHitCapsule");
        var geometry = assembly.GetType("FirepowerLoop.Arcade.ArcadeHitGeometry");
        object P(double x, double y, double z) => Activator.CreateInstance(point!, x,y,z)!;
        object C(double ax,double ay,double az,double bx,double by,double bz,double r) => Activator.CreateInstance(capsule!,P(ax,ay,az),P(bx,by,bz),r)!;
        void Sweep(double[] f,double[] t,object c,double r,bool hit,double expected=0)
        {
            object[] values={P(f[0],f[1],f[2]),P(t[0],t[1],t[2]),r,c,0d};
            bool actual=(bool)geometry!.GetMethod("TrySweepSphere")!.Invoke(null,values)!;
            if(actual!=hit || (hit && Math.Abs((double)values[4]-expected)>1e-9)) throw new Exception($"hit={actual}, fraction={values[4]}, expected={hit}/{expected}");
        }
        object Vertical() => C(0,-1,0,0,1,0,1);
        var checks = new (string,Action)[] {
            ("SideEarliest",()=>Sweep(new[]{-3d,0,0},new[]{3d,0,0},Vertical(),0,true,1d/3)),
            ("UnderwaterMiss",()=>Sweep(new[]{-3d,3,0},new[]{3d,3,0},Vertical(),0,false)),
            ("SphereRadius",()=>Sweep(new[]{-3d,0,0},new[]{3d,0,0},Vertical(),.5,true,.25)),
            ("Tangent",()=>Sweep(new[]{-3d,0,1},new[]{3d,0,1},Vertical(),0,true,.5)),
            ("LongSegment",()=>Sweep(new[]{-1e8,0,0},new[]{1e8,0,0},Vertical(),0,true,(1e8-1)/2e8)),
            ("StartInside",()=>Sweep(new[]{0d,0,0},new[]{3d,0,0},Vertical(),0,true)),
            ("ZeroSweepMiss",()=>Sweep(new[]{3d,0,0},new[]{3d,0,0},Vertical(),0,false)),
            ("ZeroCapsule",()=>Sweep(new[]{-3d,0,0},new[]{3d,0,0},C(0,0,0,0,0,0,1),0,true,1d/3)),
            ("TiltedCapsule",()=>Sweep(new[]{0d,0,-3},new[]{0d,0,3},C(-1,-1,0,1,1,0,1),0,true,1d/3)),
            ("AxialCap",()=>Sweep(new[]{0d,4,0},new[]{0d,-4,0},Vertical(),0,true,.25)),
            ("InvalidFinite",()=>{Sweep(new[]{double.NaN,0,0},new[]{3d,0,0},Vertical(),0,false);Sweep(new[]{-3d,0,0},new[]{3d,0,0},Vertical(),-1,false);Sweep(new[]{-3d,0,0},new[]{3d,0,0},C(0,0,0,0,1,0,double.PositiveInfinity),0,false);}),
            ("OverlapBoundary",()=>{if(!(bool)geometry!.GetMethod("OverlapsSphere")!.Invoke(null,new[]{P(2,0,0),(object)1d,Vertical()})!)throw new Exception("Boundary must overlap");})
        };
        int failures=0;
        foreach(var (name,check) in checks)try {if(geometry==null)throw new Exception("Production geometry is missing");check();Console.WriteLine("PASS "+name);}catch(Exception e){failures++;Console.WriteLine("FAIL "+name+": "+e.Message);}
        Console.WriteLine($"ArcadeHitGeometryChecks: {checks.Length-failures}/{checks.Length} passed");return failures==0?0:1;
    }
}
