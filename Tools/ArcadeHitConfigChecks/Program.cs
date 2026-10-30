using FirepowerLoop.Arcade;
using System.Globalization;

// 只读取正式 CSV 的六项定向检查，不创建替代配置。
internal static class Program
{
    // 验证三个正式身体采样在实际年龄、高度与射线下的几何行为。
    private static int Main(string[] args)
    {
        if(args.Length!=3||args[0]!="testClass=ArcadeHitConfigChecks"||args[1]!="maxTests=6"||args[2]!="includePassingTests=true")return 2;
        var rows=File.ReadLines("Config/DataCsv/arcade_dock_hit_pose.csv").Where(l=>!l.StartsWith("#")&&!string.IsNullOrWhiteSpace(l)).Select(l=>l.Split(',',6)).ToArray();
        var poses=rows.Select(r=>ArcadeHitPoseSequence.Parse(int.Parse(r[3]),int.Parse(r[4]),double.Parse(r[2],CultureInfo.InvariantCulture),r[5].Trim('"'))).ToArray();
        void Require(bool value,string reason){if(!value)throw new Exception(reason);}
        bool Hit(ArcadeHitPoseSequence p,double age,double height)=>p.TrySweepSphere(age,new(-3,height,0),new(3,height,0),0,0,0,0,1,out _);
        var checks=new(string,Action)[]{
            ("ThreeModels",()=>Require(rows.Select(r=>r[1]).OrderBy(s=>s).SequenceEqual(new[]{"Prefabs/Characters/ArcadeZombieBloated","Prefabs/Characters/ArcadeZombieGaunt","Prefabs/Characters/ArcadeZombieShambler"}),"Expected three formal addresses")),
            ("SampleDimensions",()=>Require(poses.All(p=>p.FrameCount>=2&&p.CapsulesPerFrame==11&&p.IntervalSeconds<=.01&&Math.Abs((p.FrameCount-1)*p.IntervalSeconds-6.75)<1e-6),"Expected 11 capsules covering 6.75 seconds with at most .01 second spacing")),
            ("EarlyBulletPassesAbove",()=>Require(poses.All(p=>!Hit(p,0,.75)),"Early .75 ray intersected body")),
            ("LateBulletHits",()=>Require(poses.All(p=>Hit(p,6.5,.75)),"Late .75 ray missed body")),
            ("SameAgeDifferentHeight",()=>Require(poses.All(p=>Hit(p,6.5,.75)&&!Hit(p,6.5,4)),"Height had no effect")),
            ("RealSampleInterpolation",()=>{foreach(var p in poses){var a=p.Sample(0,0);var b=p.Sample(p.IntervalSeconds,0);var m=p.Sample(p.IntervalSeconds*.5,0);Require(Math.Abs(m.A.Y-(a.A.Y+b.A.Y)*.5)<1e-12&&Math.Abs(m.B.Z-(a.B.Z+b.B.Z)*.5)<1e-12,"Interpolated capsule differs from midpoint");}})
        };
        int failed=0;foreach(var(name,check)in checks)try{check();Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e.Message);}
        Console.WriteLine($"ArcadeHitConfigChecks: {6-failed}/6 passed");return failed==0?0:1;
    }
}
