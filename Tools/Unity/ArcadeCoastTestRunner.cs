using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

/// <summary>标准测试工具不可用时使用 Unity 原生 API；仅允许真实限定类，先检查数量并保存全部通过结果。</summary>
public static class ArcadeCoastTestRunner
{
    private const string Output="Temp/CoastChecks";
    public static string Region()=>Run("ArcadeIslandRegionTests",3,true);
    public static string Sea()=>Run("IslandSeaApproachTests",14,true);
    public static string Presentation()=>Run("IslandRoundPresentationTests",14,true);
    public static string Transition()=>Run("ArcadeRoundTransitionTests",5,true);
    public static string Layout()=>Run("ArcadeIslandLayoutTests",15,true);
    public static string Loading()=>Run("UnifiedMainMenuTests",25,true);
    private static string Run(string testClass,int maximum,bool includePassingTests)
    {
        if(Application.dataPath.Replace('\\','/')!="E:/GitHub/FirepowerLoop/Client/Assets"||Application.isPlaying)throw new InvalidOperationException("仅本项目编辑态限定测试");
        if(!includePassingTests||string.IsNullOrWhiteSpace(testClass))throw new ArgumentException("必须限定类并包含通过结果");
        for(int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++)if(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("场景脏，不能执行测试");
        Directory.CreateDirectory(Output);string path=Output+"/"+testClass+".txt";
        var api=ScriptableObject.CreateInstance<TestRunnerApi>();
        api.RegisterCallbacks(new Results(api,path,maximum));
        File.WriteAllText(path,"Discovering "+testClass+" maximum="+maximum+" includePassingTests=true\n");
        api.RetrieveTestList(TestMode.EditMode,tree=>
        {
            var fixtures=Flatten(tree).Where(t=>t.FullName=="Game.Tests."+testClass).ToArray();
            int count=fixtures.Sum(f=>Flatten(f).Count(t=>!t.IsSuite));
            if(fixtures.Length!=1||count<1||count>maximum){File.AppendAllText(path,"REJECTED selected="+count);UnityEngine.Object.DestroyImmediate(api);return;}
            File.AppendAllText(path,"Selected="+count+"\n");
            api.Execute(new ExecutionSettings(new Filter{testMode=TestMode.EditMode,testNames=new[]{fixtures[0].FullName}}));
        });
        return path;
    }
    private static IEnumerable<ITestAdaptor> Flatten(ITestAdaptor node){yield return node;foreach(var c in node.Children)foreach(var child in Flatten(c))yield return child;}
    private sealed class Results:ICallbacks
    {
        private readonly TestRunnerApi api;private readonly string path;private readonly int maximum;private int count;
        public Results(TestRunnerApi api,string path,int maximum){this.api=api;this.path=path;this.maximum=maximum;}
        public void RunStarted(ITestAdaptor testsToRun){}
        public void TestStarted(ITestAdaptor test){}
        public void TestFinished(ITestResultAdaptor result)
        {
            if(result.Test.IsSuite)return;
            count++;File.AppendAllText(path,result.Test.FullName+" "+result.TestStatus+" "+result.Message+"\n");
        }
        public void RunFinished(ITestResultAdaptor result)
        {
            File.AppendAllText(path,"FINISHED executed="+count+" passed="+result.PassCount+" failed="+result.FailCount+" maximum="+maximum+"\n");
            api.UnregisterCallbacks(this);UnityEngine.Object.DestroyImmediate(api);
        }
    }
}
