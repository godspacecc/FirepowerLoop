using System;
using System.IO;
using System.Linq;
using Luban;
using NUnit.Framework;
using QFramework;
using UnityEngine;
using FirepowerLoop.Arcade;
namespace Game.Tests
{
    /// <summary>真实结算和存档走完新增三关后的十三关五十二轮，保留分数与本局结果。</summary>
    public sealed class ArcadeEarlyStageRouteTests
    {
        [Test] public void ExpandedVersionRunCompletesThirteenStagesThroughNextThreeAndKeepsResult()
        {
            string directory=Path.Combine(Path.GetTempPath(),"FirepowerEarlyRoute-"+Guid.NewGuid().ToString("N"));
            try
            {
                var app=new Game();var model=new ArcadeRunModel();app.RegisterModel(model);
                var provider=new LubanRuntimeConfigProvider(()=>new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes")))));app.RegisterUtility<IRuntimeConfigProvider>(provider);
                Assert.That(provider.TryGetArcadeCatalog(out var catalog,out var error),Is.True,error);var system=new ArcadeRunSystem();app.RegisterSystem(system);system.ConfigureForTests(directory);
                // 明确构造已发布版本1旧局，再从真实文件恢复；正式新局另由TenStageRouteTests覆盖。
                var data=new ArcadeRunSaveData{RunId=Guid.NewGuid().ToString("N"),IsCasualRoute=true,CasualRouteVersion=ArcadeRouteRules.ExpandedVersion,
                    ArenaConfigNumber=1,NextArenaNumber=1,UpdatedAtUtc=DateTime.UtcNow.ToString("O"),Checkpoint=new ArcadeCheckpointSaveData{ArenaNumber=1}};
                data.EquipmentBySlot[ArcadeRunDefaults.WeaponSlotId]=catalog.Rule.BaseWeaponId;
                Assert.That(new ArcadeRunFileStore(directory).Save(data,out error),Is.True,error);
                Assert.That(system.TryResume(out data,out error),Is.True,error);
                int[] route={1,21,22,23,24,25,5,6,7,9,11,12,20};int rounds=0;
                for(int index=0;index<route.Length;index++)
                {
                    Assert.That(data.ArenaConfigNumber,Is.EqualTo(route[index]));Assert.That(data.NextArenaNumber,Is.EqualTo(index+1));model.EnterArena();
                    Assert.That(catalog.RoundLayouts.Count(p=>p.ArenaConfigNumber==data.ArenaConfigNumber),Is.EqualTo(4));rounds+=4;
                    bool final=index==route.Length-1;int next=final?0:route[index+1];var completion=new ArcadeArenaCompletion{ArenaNumber=index+1,NextArenaConfigNumber=next,Victory=final,
                        Score=(index+1)*100,ScoreMultiplier=1,EarnedGold=1,NextCheckpoint=new ArcadeCheckpointSaveData{ArenaNumber=index+2},EquipmentBySlot=data.EquipmentBySlot,EmergencyResources=data.EmergencyResources};
                    Assert.That(system.TryCompleteArena(completion,out data,out error),Is.True,error);Assert.That(new ArcadeRunFileStore(directory).Inspect().IsValid,Is.True);
                }
                Assert.That(rounds,Is.EqualTo(52));Assert.That(data.CasualRouteVersion,Is.EqualTo(ArcadeRouteRules.ExpandedVersion));Assert.That(data.Victory,Is.True);Assert.That(data.Score,Is.EqualTo(1300));Assert.That(data.CompletedArenaCount,Is.EqualTo(13));Assert.That(data.PendingRewards,Is.Empty);
            }
            finally{if(Directory.Exists(directory))Directory.Delete(directory,true);}
        }
    }
}
