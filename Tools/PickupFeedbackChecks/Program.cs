using System;
using Game;

// 定向纯 C# 检查：明确过滤及数量上限，不启动 Unity 或 Player Build。
if (args.Length != 3 || args[0] != "--testClass=PickupFeedbackChecks" || args[1] != "--maxTests=7" || args[2] != "--includePassingTests=true")
    throw new ArgumentException("必须显式声明测试类、最大数量及通过项输出");
int failed = 0;
void Check(string name, Action check)
{
    try { check(); Console.WriteLine("PASS " + name); }
    catch (Exception e) { failed++; Console.WriteLine("FAIL " + name + ": " + e.Message); }
}
void Require(bool condition) { if (!condition) throw new Exception("结果不符合预期"); }
Check("ActualScoreSum", () => {
    var b = new ArcadePickupFeedbackBuffer();
    Require(b.TryAdd("score", 150, 0, out var key, out var sum) && sum == 150);
    Require(b.TryAdd("score", 275, .1, out var next, out sum) && sum == 425 && key == next);
});
Check("ScoreAndGoldStaySeparate", () => {
    var b = new ArcadePickupFeedbackBuffer(); b.TryAdd("score", 500, 0, out var score, out _);
    Require(b.TryAdd("gold", 3, .1, out var gold, out var sum) && score != gold && sum == 3);
});
Check("DifferentProductsStaySeparate", () => {
    var b = new ArcadePickupFeedbackBuffer(); b.TryAdd("weapon.spread", 1, 0, out var first, out _);
    Require(b.TryAdd("powerup.shield", 1, .1, out var next, out var sum) && first != next && sum == 1);
});
Check("InvalidRewardsStaySilent", () => {
    var b = new ArcadePickupFeedbackBuffer();
    Require(!b.TryAdd("score", 0, 0, out _, out _));
    Require(!b.TryAdd("score", -4, 0, out _, out _));
    Require(!b.TryAdd("", 4, 0, out _, out _));
});
Check("MergeWindowExpires", () => {
    var b = new ArcadePickupFeedbackBuffer(); b.TryAdd("score", 100, 0, out var old, out _);
    Require(b.TryAdd("score", 200, 1, out var next, out var sum) && next != old && sum == 200);
});
Check("SoundRateLimited", () => {
    var b = new ArcadePickupFeedbackBuffer();
    Require(b.TrySound(0)); Require(!b.TrySound(0)); Require(!b.TrySound(.05)); Require(b.TrySound(.2));
});
Check("ClearDropsPreviousMap", () => {
    var b = new ArcadePickupFeedbackBuffer(); b.TryAdd("score", 100, 0, out var old, out _); b.TrySound(0); b.Clear();
    Require(b.TryAdd("score", 25, .1, out var next, out var sum) && sum == 25 && old != next);
    Require(b.TrySound(.1));
});
Console.WriteLine($"PickupFeedbackChecks: {7 - failed}/7 passed");
return failed == 0 ? 0 : 1;
