using System.Reflection;
using FirepowerLoop.Arcade;

// 八项具名几何采样检查，不依赖 Unity 或运行时伤害调用。
internal static class Program
{
    // 限定检查范围并输出通过项，禁止无效过滤回退。
    private static int Main(string[] args)
    {
        if (args.Length!=3 || args[0]!="testClass=ArcadeHitPoseChecks" || args[1]!="maxTests=8" || args[2]!="includePassingTests=true")return 2;
        var type=Assembly.GetExecutingAssembly().GetType("FirepowerLoop.Arcade.ArcadeHitPoseSequence");
        object Parse(string data,int n=1)=>type!.GetMethod("Parse")!.Invoke(null,new object[]{2,n,1d,data})!;
        object Pose()=>Parse("1|0|0|1|1|0|0.25|1|2|0|1|3|0|0.25");
        double Hit(object pose,double age,ArcadeHitPoint from,ArcadeHitPoint to,double fx=0,double fz=1)
        {object[] p={age,from,to,0d,0d,0d,fx,fz,0d};return (bool)type!.GetMethod("TrySweepSphere")!.Invoke(pose,p)!?(double)p[8]:-1;}
        void Eq(double a,double b){if(Math.Abs(a-b)>1e-9)throw new Exception($"expected {b}, actual {a}");}
        var checks=new (string,Action)[]{
            ("HeightDependent",()=>{var p=Pose();Eq(Hit(p,0,new(1,.5,-2),new(1,.5,2)),.4375);Eq(Hit(p,0,new(1,3,-2),new(1,3,2)),-1);}),
            ("FacingDependent",()=>{var p=Pose();Eq(Hit(p,0,new(1,.5,-2),new(1,.5,2),1,0),-1);Eq(Hit(p,0,new(-2,.5,-1),new(2,.5,-1),1,0),.4375);}),
            ("InterpolatedPose",()=>Eq(Hit(Pose(),.5,new(1,1.5,-2),new(1,1.5,2)),.4375)),
            ("FrozenRepeat",()=>{var p=Pose();Eq(Hit(p,.5,new(1,1.5,-2),new(1,1.5,2)),Hit(p,.5,new(1,1.5,-2),new(1,1.5,2)));}),
            ("StartInside",()=>Eq(Hit(Pose(),0,new(1,.5,0),new(1,.5,3)),0)),
            ("MultipleEarliest",()=>{string f="0|0|1|0|1|1|0.25|0|0|-1|0|1|-1|0.25";Eq(Hit(Parse(f+"|"+f,2),0,new(0,.5,-3),new(0,.5,3)),1.75/6);}),
            ("ClampAndOverlap",()=>{var p=Pose();Eq(Hit(p,-10,new(1,.5,-2),new(1,.5,2)),.4375);if(!(bool)type!.GetMethod("OverlapsSphere")!.Invoke(p,new object[]{10d,new ArcadeHitPoint(1,2.5,0),0d,0d,0d,0d,1d})!)throw new Exception("Clamped overlap missing");}),
            ("RejectBadData",()=>{foreach(string data in new[]{"0|0", "0|0|0|0|1|0|-1|0|0|0|0|1|0|1", "NaN|0|0|0|1|0|1|0|0|0|0|1|0|1"}){bool rejected=false;try{Parse(data);}catch(TargetInvocationException e) when(e.InnerException is ArgumentException){rejected=true;}if(!rejected)throw new Exception("Bad data accepted");}})
        };
        int failed=0;foreach(var(name,check)in checks)try{if(type==null)throw new Exception("Production pose sequence missing");check();Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e.Message);}
        Console.WriteLine($"ArcadeHitPoseChecks: {8-failed}/8 passed");return failed==0?0:1;
    }
}
