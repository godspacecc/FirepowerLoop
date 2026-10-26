using System.Reflection;
using ET;

// 只接受真实短类和固定三项范围；无效过滤明确失败，不回退完整套件。
var options=new Dictionary<string,string>(StringComparer.Ordinal);
foreach(var arg in args){var pair=arg.Split('=',2);if(pair.Length!=2||!options.TryAdd(pair[0],pair[1]))return 2;}
if(options.Count!=3||options.GetValueOrDefault("testClass")!="TenStageEnvironmentOnline"||options.GetValueOrDefault("maxTests")!="3"||options.GetValueOrDefault("includePassingTests")!="true")
{Console.Error.WriteLine("Requires testClass=TenStageEnvironmentOnline maxTests=3 includePassingTests=true");return 2;}
return TenStageEnvironmentOnline.Run();

/// <summary>服务端真实配置启动及缺项/重复拒绝契约；不启动监听，不读取玩家数据。</summary>
internal static class TenStageEnvironmentOnline
{
    private static ServerConfig Load()=>ServerConfig.CreateForValidation("OnlineLocal",n=>File.ReadAllBytes(Path.Combine("Server/GameServer/Config/Luban",n+".bytes")));
    public static int Run()
    {
        var tests=new (string Name,Action Run)[]{("LoadsAllAdoptedEnvironments",Complete),("RejectsMissingEnvironment",()=>Invalid(false)),("RejectsDuplicatedEnvironment",()=>Invalid(true))};
        int failed=0;foreach(var test in tests)try{test.Run();Console.WriteLine("PASS TenStageEnvironmentOnline."+test.Name);}catch(Exception e){failed++;Console.WriteLine("FAIL TenStageEnvironmentOnline."+test.Name+": "+(e.InnerException??e));}
        Console.WriteLine($"{tests.Length-failed}/{tests.Length} passed");return failed==0?0:1;
    }
    private static void Complete()
    {
        var config=Load();
        Require(config.EarlyStages.Keys.OrderBy(p=>p).SequenceEqual(new[]{5,7,10,20,21,22,23,24,25}),"adopted environment set mismatch");
        foreach(int arena in new[]{7,10,20}){var stage=config.EarlyStages[arena];Require(stage.HalfWidth==19.5f&&stage.HalfHeight==19.5f&&stage.HerdCount==0,"visual adoption changed combat or herd rules");}
    }
    private static void Invalid(bool duplicate)
    {
        var config=Load();var rows=config.Tables.TbArcadeEarlyStage.DataList;var fort=rows.Single(p=>p.ArenaConfigNumber==7);
        if(duplicate)rows.Add(fort);else rows.Remove(fort);
        try{typeof(ServerConfig).GetMethod("ValidateArcadeConfig",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(config,null);}
        catch(TargetInvocationException e)when(e.InnerException!=null&&e.InnerException.Message.Contains("Table=TbArcadeEarlyStage")&&e.InnerException.Message.Contains("expected adopted environments")){return;}
        throw new InvalidOperationException("invalid adopted directory was accepted or failed for wrong reason");
    }
    private static void Require(bool valid,string message){if(!valid)throw new InvalidOperationException(message);}
}
