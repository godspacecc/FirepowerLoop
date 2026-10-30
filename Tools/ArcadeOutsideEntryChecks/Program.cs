using FirepowerLoop.Arcade;

// 仅运行本文件十项纯规则检查；不连接 Unity，也不回退到其他测试。
var tests = new (string Name, Action Run)[] {
    ("OutsideStartPreserved", () => { var s=Start(); Check(ArcadeOutsideEntryRules.Advance(ref s,0,2,out var p)); Near(p.X,20); Near(p.Y,4); }),
    ("ContinuousApproach", () => { var s=Start(); ArcadeOutsideEntryRules.Advance(ref s,1,2,out var p); Near(p.X,18); Near(p.Y,4); Near(s.DistanceMoved,2); }),
    ("CompletionFrameOwned", () => { var s=Start(); Check(ArcadeOutsideEntryRules.Advance(ref s,5,2,out var p)); Check(!s.Active); Near(p.X,10); Check(!ArcadeOutsideEntryRules.Advance(ref s,1,2,out p)); Near(p.X,10); }),
    ("OvershootStopsAtEntry", () => { var s=Start(); ArcadeOutsideEntryRules.Advance(ref s,float.MaxValue,float.MaxValue,out var p); Near(p.X,10); Near(s.DistanceMoved,10); }),
    ("ZeroTimeFreezes", () => { var s=Start(); ArcadeOutsideEntryRules.Advance(ref s,1,2,out _); Check(ArcadeOutsideEntryRules.Advance(ref s,0,2,out var p)); Near(p.X,18); }),
    ("InvalidTimeRejected", () => { foreach(var t in new[]{-1f,float.NaN,float.PositiveInfinity}) { var s=Start(); Check(ArcadeOutsideEntryRules.Advance(ref s,t,2,out var p)); Near(p.X,20); Near(s.DistanceMoved,0); } }),
    ("InvalidSpeedRejected", () => { foreach(var v in new[]{-1f,float.NaN,float.PositiveInfinity,0f}) { var s=Start(); Check(ArcadeOutsideEntryRules.Advance(ref s,1,v,out var p)); Near(p.X,20); Near(s.DistanceMoved,0); } }),
    ("InvalidStartRejected", () => { Check(!ArcadeOutsideEntryRules.TryStart(1,2,1,2,out _)); Check(!ArcadeOutsideEntryRules.TryStart(float.NaN,0,1,0,out _)); Check(!ArcadeOutsideEntryRules.TryStart(1,0,float.PositiveInfinity,0,out _)); Check(!ArcadeOutsideEntryRules.TryStart(-float.MaxValue,0,float.MaxValue,0,out _)); }),
    ("DiagonalAndSplitSteps", () => { Check(ArcadeOutsideEntryRules.TryStart(3,4,0,0,out var s)); ArcadeOutsideEntryRules.Advance(ref s,.5f,2,out var p); Near(p.X,2.4f); Near(p.Y,3.2f); ArcadeOutsideEntryRules.Advance(ref s,2,2,out p); Near(p.X,0); Near(p.Y,0); Check(!s.Active); }),
    ("InactiveStateNoOwnership", () => { var s=default(ArcadeOutsideEntryState); Check(!ArcadeOutsideEntryRules.Advance(ref s,1,2,out var p)); Near(p.X,0); Near(p.Y,0); })
};
int failed=0;
foreach(var test in tests) { try { test.Run(); Console.WriteLine("PASS "+test.Name); } catch(Exception e) { failed++; Console.WriteLine("FAIL "+test.Name+": "+e.Message); } }
Console.WriteLine($"Scoped checks: {tests.Length}; passed: {tests.Length-failed}; failed: {failed}; includePassingTests=true");
return failed==0?0:1;
static ArcadeOutsideEntryState Start() { Check(ArcadeOutsideEntryRules.TryStart(20,4,10,4,out var s)); return s; }
static void Check(bool value) { if(!value) throw new Exception("Assertion failed"); }
static void Near(float actual,float expected) { if(!float.IsFinite(actual)||Math.Abs(actual-expected)>.0001f) throw new Exception($"Expected {expected}, got {actual}"); }
