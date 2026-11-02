using System;
using System.Linq;
using NUnit.Framework;
namespace Game.Tests
{
    /// <summary>可操作任务预览的流程规则，不代表正式战斗集成验证。</summary>
    public sealed class MissionPreviewTests
    {
        private object New(){var t=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("FirepowerLoop.Preview.MissionPreviewState")).FirstOrDefault(x=>x!=null);Assert.That(t,Is.Not.Null,"任务预览规则尚未实现");return Activator.CreateInstance(t);}
        private object Call(object s,string n,params object[] args)=>s.GetType().GetMethod(n).Invoke(s,args);
        private object Read(object s,string n)=>s.GetType().GetProperty(n).GetValue(s);
        [Test] public void BriefingDoesNotAcceptCollection(){var s=New();Assert.That(Call(s,"Collect",0,true),Is.False);Assert.That(Read(s,"Phase").ToString(),Is.EqualTo("Briefing"));}
        [Test] public void CollectionRequiresRangeAndUniqueIdentity(){var s=New();Call(s,"Start");Assert.That(Call(s,"Collect",0,false),Is.False);Assert.That(Call(s,"Collect",0,true),Is.True);Assert.That(Call(s,"Collect",0,true),Is.False);Assert.That(Call(s,"Collect",3,true),Is.False);Assert.That(Read(s,"Collected"),Is.EqualTo(1));}
        [Test] public void ThirdSupplyTriggersOneAmbush(){var s=New();Call(s,"Start");for(int i=0;i<3;i++)Call(s,"Collect",i,true);Assert.That(Read(s,"Phase").ToString(),Is.EqualTo("Ambush"));Assert.That(Call(s,"Collect",2,true),Is.False);}
        [Test] public void DuplicateDeathsDoNotSkipAmbush(){var s=New();Call(s,"Start");for(int i=0;i<3;i++)Call(s,"Collect",i,true);Assert.That(Call(s,"Defeat",0),Is.True);Assert.That(Call(s,"Defeat",0),Is.False);Assert.That(Call(s,"Defeat",10),Is.False);Assert.That(Read(s,"Remaining"),Is.EqualTo(9));}
        [Test] public void ExitRequiresClearedAmbushAndRange(){var s=New();Call(s,"Start");Assert.That(Call(s,"Extract",true),Is.False);for(int i=0;i<3;i++)Call(s,"Collect",i,true);for(int i=0;i<10;i++)Call(s,"Defeat",i);Assert.That(Call(s,"Extract",false),Is.False);Assert.That(Call(s,"Extract",true),Is.True);Assert.That(Call(s,"Extract",true),Is.False);Assert.That(Read(s,"Phase").ToString(),Is.EqualTo("Success"));}
        [Test] public void FailureStopsCollectionAndExtraction(){var s=New();Call(s,"Start");Call(s,"Damage",100);Assert.That(Read(s,"Phase").ToString(),Is.EqualTo("Failed"));Assert.That(Call(s,"Collect",0,true),Is.False);Assert.That(Call(s,"Extract",true),Is.False);}
        [Test] public void RestartRestoresBriefingAndDoesNotCarrySupplies(){var s=New();Call(s,"Start");Call(s,"Collect",0,true);Call(s,"Damage",30);Call(s,"Reset");Assert.That(Read(s,"Health"),Is.EqualTo(100));Assert.That(Read(s,"Collected"),Is.Zero);Assert.That(Read(s,"Phase").ToString(),Is.EqualTo("Briefing"));}
    }
}
