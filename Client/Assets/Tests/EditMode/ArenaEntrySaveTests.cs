using System;
using System.IO;
using System.Linq;
using Luban;
using NUnit.Framework;
using QFramework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>海岛最终出口到后续主题的角色连续性与文件事务，只使用独立临时目录。</summary>
    public sealed class ArenaEntrySaveTests
    {
        private ArcadeRunModel model;
        private ArcadeRunSystem system;
        private ArcadeRunFileStore store;
        private ArcadeConfigCatalogDefinition catalog;
        private string root;
        [SetUp] public void Setup()
        {
            root=Path.Combine(Path.GetTempPath(),"FirepowerLoop-ArenaEntry-"+Guid.NewGuid().ToString("N"));
            var architecture=new Game();model=new ArcadeRunModel();architecture.RegisterModel(model);
            var provider=new LubanRuntimeConfigProvider(()=>new Tables(name=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",name+".bytes")))));
            Assert.That(provider.TryGetArcadeCatalog(out catalog,out var error),Is.True,error);
            architecture.RegisterUtility<IRuntimeConfigProvider>(provider);
            system=new ArcadeRunSystem();architecture.RegisterSystem(system);system.ConfigureForTests(root);store=new ArcadeRunFileStore(root);
            Assert.That(system.TryStartConfiguredRun(out _,out error),Is.True,error);
            Assert.That(system.TryContinue(out _,out error),Is.True,error);
        }
        [TearDown] public void Cleanup(){if(Directory.Exists(root))Directory.Delete(root,true);}

        [Test] public void IslandExitPersistsExactRoleAndEffectsWithoutOldArenaGold()
        {
            var entry=Entry();var completion=Completion(entry);
            Assert.That(system.TryCompleteArena(completion,out var saved,out var error),Is.True,error);
            entry.PlayerHealth=100;entry.Powerups[0].Charges=99;
            Assert.That(saved.ArenaEntryState.PlayerHealth,Is.EqualTo(80));
            Assert.That(model.Current.ArenaEntryState.Powerups[0].Charges,Is.EqualTo(2));
            Assert.That(saved.RunGold,Is.EqualTo(17));Assert.That(saved.ArenaEntryState.ArenaGold,Is.Zero);
            Assert.That(system.TryResume(out saved,out error),Is.True,error);
            Assert.That(saved.ArenaEntryState.TemporaryWeaponRemaining,Is.EqualTo(1.5f));
            Assert.That(saved.ArenaEntryState.Powerups[0].RemainingSeconds,Is.EqualTo(2.5f));
            Assert.That(saved.IslandRoundCheckpoint,Is.Null);
        }

        [Test] public void FailedExitWriteKeepsSourceNodeAndRetryDoesNotDuplicateGold()
        {
            for(int round=1;round<=4;round++)
            {var node=Entry();node.MapId="island.round."+round;node.RoundNumber=round;node.ArenaGold=17;Assert.That(system.TrySaveIslandRound(node,out _),Is.True);}
            using(var locked=new FileStream(store.RunPath,FileMode.Open,FileAccess.Read,FileShare.None))
            {Assert.That(system.TryCompleteArena(Completion(Entry()),out _,out _),Is.False);Assert.That(model.Current.IslandRoundCheckpoint.RoundNumber,Is.EqualTo(4));Assert.That(model.Current.ArenaEntryState,Is.Null);}
            Assert.That(store.Inspect().Data.IslandRoundCheckpoint.RoundNumber,Is.EqualTo(4));
            Assert.That(system.TryCompleteArena(Completion(Entry()),out var saved,out var error),Is.True,error);
            Assert.That(saved.RunGold,Is.EqualTo(17));Assert.That(saved.ArenaEntryState.ArenaGold,Is.Zero);
            Assert.That(system.TryCompleteArena(Completion(Entry()),out _,out _),Is.False);
            Assert.That(store.Inspect().Data.RunGold,Is.EqualTo(17));
        }

        [Test] public void EntryRejectsOldGoldAndUnrelatedArenaLineage()
        {
            var entry=Entry();entry.ArenaGold=17;
            Assert.That(system.TryCompleteArena(Completion(entry),out _,out _),Is.False);
            Assert.That(model.Phase,Is.EqualTo(ArcadeRunPhase.Arena));
            var data=model.Current;data.CompletedArenaCount=1;data.NextArenaNumber=2;data.ArenaConfigNumber=2;data.Shop.ArenaNumber=2;
            model.CommitSafeNode(data);model.EnterArena();
            Assert.That(system.TryCompleteArena(Completion(Entry()),out _,out _),Is.False);
            Assert.That(model.Current.ArenaEntryState,Is.Null);
        }

        [Test] public void FollowingArenaPropagatesStateAndAbandonClearsIt()
        {
            Assert.That(system.TryCompleteArena(Completion(Entry()),out _,out var error),Is.True,error);
            Assert.That(system.TryContinue(out _,out error),Is.True,error);
            var entry=Entry();entry.PlayerHealth=40;entry.Powerups[0].Charges=1;
            Assert.That(system.TryCompleteArena(Completion(entry),out var saved,out error),Is.True,error);
            Assert.That(saved.ArenaEntryState.PlayerHealth,Is.EqualTo(40));Assert.That(saved.ArenaEntryState.Powerups[0].Charges,Is.EqualTo(1));
            Assert.That(system.TryAbandon(out error),Is.True,error);
            Assert.That(store.Inspect().Data.ArenaEntryState,Is.Null);
        }
        private ArcadeArenaCompletion Completion(ArcadeIslandRoundCheckpoint entry)
        {
            var current=model.Current;var arena=catalog.Arenas.First(a=>a.ArenaNumber==current.ArenaConfigNumber);
            return new ArcadeArenaCompletion { ArenaNumber=current.NextArenaNumber,NextArenaConfigNumber=arena.NextArenaNumbers[0],NextShop=new ArcadeShopSaveData{ArenaNumber=current.NextArenaNumber+1},EarnedGold=17,EmergencyResources=current.EmergencyResources,EquipmentBySlot=current.EquipmentBySlot,PlayerState=entry };
        }
        private static ArcadeIslandRoundCheckpoint Entry()=>new ArcadeIslandRoundCheckpoint
        {
            RoundNumber=1,MapId="arena.entry",PlayerHealth=80,PlayerMaxHealth=100,Bombs=2,Dashes=3,
            TemporaryWeaponId=ArcadeRunDefaults.BasicWeaponId,TemporaryWeaponRemaining=1.5f,
            Powerups=new System.Collections.Generic.List<ArcadeIslandPowerupCheckpoint>{new ArcadeIslandPowerupCheckpoint{ProductId="arcade.powerup.shield",RemainingSeconds=2.5f,Charges=2}}
        };
    }
}
