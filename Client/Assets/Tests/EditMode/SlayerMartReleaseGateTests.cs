using System;
using System.IO;
using System.Reflection;
using FirepowerLoop.Arcade;
using Luban;
using NUnit.Framework;
using QFramework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>正式新局读取发布配置，旧档恢复不随当前开关迁移规则。</summary>
    public sealed class SlayerMartReleaseGateTests
    {
        private Game architecture;private ArcadeRunSystem runs;private string root;
        [SetUp]public void Setup(){root=Path.Combine(Path.GetTempPath(),"FirepowerSlayerRelease-"+Guid.NewGuid().ToString("N"));architecture=new Game();architecture.RegisterModel(new ArcadeRunModel());architecture.RegisterSystem(new ArcadeRunSystem());runs=architecture.GetSystem<ArcadeRunSystem>();runs.ConfigureForTests(root);}
        [TearDown]public void Cleanup(){if(Directory.Exists(root))Directory.Delete(root,true);}
        private void Provider(bool enabled,bool supported=true,bool fail=false)=>architecture.RegisterUtility<IRuntimeConfigProvider>(new TestProvider(enabled,supported,fail));
        private ArcadeRunSaveData Start(){Assert.That(runs.TryStartConfiguredRun(out var value,out var error),Is.True,error);return value;}
        [Test]public void EnabledFormalNewRunPersistsVersionOne(){Provider(true);Assert.That(Start().SlayerMartRulesVersion,Is.EqualTo(1));Assert.That(new ArcadeRunFileStore(root).Inspect().Data.SlayerMartRulesVersion,Is.EqualTo(1));}
        [Test]public void DisabledFormalNewRunKeepsVersionZero(){Provider(false);Assert.That(Start().SlayerMartRulesVersion,Is.Zero);}
        [Test]public void LegacyProviderWithoutShopRemainsVersionZero(){Provider(true,false);Assert.That(Start().SlayerMartRulesVersion,Is.Zero);}
        [Test]public void MissingRequiredShopConfigRejectsBeforeWriting(){Provider(true,true,true);Assert.That(runs.TryStartConfiguredRun(out _,out _),Is.False);Assert.That(architecture.GetModel<ArcadeRunModel>().HasRun,Is.False);Assert.That(new ArcadeRunFileStore(root).Inspect().Status,Is.EqualTo(SaveFileStatus.Missing));}
        [Test]public void OldRunDoesNotUpgradeWhenGateOpens(){Provider(false);Start();Provider(true);Assert.That(runs.TryResume(out var value,out var error),Is.True,error);Assert.That(value.SlayerMartRulesVersion,Is.Zero);}
        [Test]public void NewRunDoesNotDowngradeWhenGateCloses(){Provider(true);Start();Provider(false);Assert.That(runs.TryResume(out var value,out var error),Is.True,error);Assert.That(value.SlayerMartRulesVersion,Is.EqualTo(1));}
        [Test]public void LegacyNewRunHelperKeepsItsOriginalContract(){Provider(true);Assert.That(runs.TryStartNewRun(new ArcadeCheckpointSaveData(),out var value,out var error),Is.True,error);Assert.That(value.SlayerMartRulesVersion,Is.Zero);}
        private sealed class TestProvider : IRuntimeConfigProvider
        {
            private readonly LubanRuntimeConfigProvider inner;private readonly bool fail;
            public bool HasSlayerMartConfiguration{get;}
            public TestProvider(bool enabled,bool supported,bool fail)
            {
                this.fail=fail;HasSlayerMartConfiguration=supported;
                var tables=new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes"))));
                // 仅调整本夹具的反序列化对象，再经正式适配校验；不写Config、生成文件或bytes。
                typeof(ArcadeSlayerMartAbilityConfig).GetField("Value").SetValue(tables.TbArcadeSlayerMartAbility.Get("release.enabled"),enabled?1f:0f);
                inner=new LubanRuntimeConfigProvider(()=>tables);
            }
            public bool TryGetMap(string id,out MapAssetDefinition value,out string error)=>inner.TryGetMap(id,out value,out error);
            public bool TryGetMapSpawns(string id,out System.Collections.Generic.IReadOnlyList<MapSpawnDefinition> value,out string error)=>inner.TryGetMapSpawns(id,out value,out error);
            public bool TryGetPlayer(string id,out PlayerConfigDefinition value,out string error)=>inner.TryGetPlayer(id,out value,out error);
            public bool TryGetEnemy(string id,out EnemyConfigDefinition value,out string error)=>inner.TryGetEnemy(id,out value,out error);
            public bool TryGetLoot(string id,out LootConfigDefinition value,out string error)=>inner.TryGetLoot(id,out value,out error);
            public bool TryGetArcadeCatalog(out ArcadeConfigCatalogDefinition value,out string error)=>inner.TryGetArcadeCatalog(out value,out error);
            public bool TryGetSlayerMart(out ArcadeSlayerMartConfiguration value,out string error){if(fail){value=null;error="required shop unavailable";return false;}return inner.TryGetSlayerMart(out value,out error);}
        }
    }
}
