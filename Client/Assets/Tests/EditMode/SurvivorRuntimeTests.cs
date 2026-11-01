using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using FirepowerLoop.Arcade;
using Luban;
using NUnit.Framework;
using QFramework;
using UnityEngine;
namespace Game.Tests
{
    /// <summary>幸存者限定方法验证；真实配置、假表现、独立临时存档。</summary>
    public sealed class SurvivorRuntimeTests
    {
        private Game architecture;
        private SurvivorSystem system;
        private SurvivorModel model;
        private ArcadeRunModel arcade;
        private SurvivorConfiguration config;
        private TestView view;
        private PlayerInputCtrl input;
        private string root;
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        /// <summary>正式抽取排除停用项，旧请求不能偷偷提升主武器。</summary>
        [Test] public void DisabledMainWeaponChoicesAreNotOfferedOrAccepted()
        {
            model.State.AddExperience(config.InitialExperienceThreshold);Call("Offer");
            foreach(int id in model.UpgradeChoices)Assert.That(config.Upgrades[id-1].Weight,Is.GreaterThan(0));
            foreach(string kind in new[]{"Rifle","Damage","FireRate"})
            {
                int id=Enumerable.Range(1,config.Upgrades.Count).Single(i=>config.Upgrades[i-1].Kind==kind);
                Assert.That(model.UpgradeChoices,Has.No.Member(id));
                Assert.That(system.Choose(0,id),Is.False);
            }
            Assert.That(model.WeaponLevel("Rifle"),Is.EqualTo(1));
        }
        /// <summary>快速弹夹等有限项升满后，生命仍能完成下一次强化，不因只剩一项而卡住。</summary>
        [Test] public void MaxedLimitedUpgradesLeaveUsableHealthChoice()
        {
            for(int i=0;i<5;i++)model.Loadout.TryUpgrade("Drone",5);
            foreach(int id in Enumerable.Range(1,config.Upgrades.Count).Where(i=>config.Upgrades[i-1].Weight>0&&!config.Upgrades[i-1].IsWeapon&&config.Upgrades[i-1].MaxLevel>0))
                for(int level=0;level<config.Upgrades[id-1].MaxLevel;level++)
                {
                    model.State.AddExperience(model.State.RequiredExperience);
                    model.State.OfferWeighted(new[]{id},new[]{1},1,0);
                    Assert.That(model.State.Choose(model.State.ChoiceRevision,id),Is.True);
                }
            model.State.AddExperience(model.State.RequiredExperience);Call("Offer");
            Assert.That(model.UpgradeChoices.Select(i=>config.Upgrades[i-1].Kind),Is.EquivalentTo(new[]{"Health"}));
            Assert.That(system.Choose(model.State.ChoiceRevision,model.UpgradeChoices[0]),Is.True);
            Assert.That(model.Paused,Is.False);
        }
        private void Set(string name,object value)=>typeof(SurvivorSystem).GetField(name,Flags).SetValue(system,value);
        private T Get<T>(string name)=>(T)typeof(SurvivorSystem).GetField(name,Flags).GetValue(system);
        private object Call(string name,params object[] args)=>typeof(SurvivorSystem).GetMethod(name,Flags).Invoke(system,args);
        [Test] public void ReloadPresentationFollowsAuthorityAndClearsAtCompletion()
        {
            model.RifleCapacity=30;model.RifleRounds=0;model.RifleReloadDuration=2;model.RifleReloadRemaining=1.5f;
            Call("Publish");Assert.That(view.ReloadVisible,Is.True);Assert.That(view.ReloadProgress,Is.EqualTo(.25f));
            Assert.That(view.AmmoVisible,Is.False);Assert.That(view.AmmoCurrent,Is.Zero);Assert.That(view.AmmoCapacity,Is.EqualTo(30));
            system.SetPaused(true);Assert.That(view.ReloadProgress,Is.EqualTo(.25f));
            model.RifleReloadRemaining=0;model.RifleRounds=30;Call("Publish");Assert.That(view.ReloadVisible,Is.False);
            Assert.That(view.AmmoCurrent,Is.EqualTo(30));
            model.RifleReloadRemaining=1;model.Active=false;Call("Publish");Assert.That(view.ReloadVisible,Is.False);
            Assert.That(view.AmmoVisible,Is.False);
        }
        /// <summary>新强化不能给未拥有的武器，满级及旧请求不能再次领取。</summary>
        [Test] public void ModifierChoicesRespectWeaponOwnershipAndCap()
        {
            int drone=ModifierId("DronePierce"),rifle=ModifierId("RiflePierce");
            model.State.AddExperience(model.State.RequiredExperience);
            model.State.OfferWeighted(new[]{drone},new[]{1},1,0);
            Assert.That(system.Choose(model.State.ChoiceRevision,drone),Is.False);
            // 从另一轮干净状态验证合法武器独立升满。
            model.State=new SurvivorRunState(10,5);
            for(int i=0;i<3;i++)GrantModifier("RiflePierce");
            model.State.AddExperience(model.State.RequiredExperience);Call("Offer");
            Assert.That(model.UpgradeChoices,Has.No.Member(rifle));
            Assert.That(model.UpgradeChoices,Has.No.Member(drone));
            Assert.That(system.Choose(model.State.ChoiceRevision,rifle),Is.False);
        }
        [Test] public void ModifierShotSnapshotsAreIndependentPerWeapon()
        {
            GrantModifier("RiflePierce");
            Call("Shoot",Vector2.zero,Vector2.up,config.WeaponLevel("Rifle",1),"Rifle");
            var shots=Get<Dictionary<int,ArcadeProjectileRuntime>>("bullets");
            var first=shots.Values.Single();Assert.That(first.RemainingHits,Is.EqualTo(2));
            GrantModifier("RiflePierce");Assert.That(first.RemainingHits,Is.EqualTo(2),"在途弹不追改");
            model.Loadout.TryUpgrade("Drone",5);
            Call("Shoot",Vector2.zero,Vector2.up,config.WeaponLevel("Drone",1),"Drone");
            Assert.That(shots.Values.Single(b=>b.SourceWeaponId=="survivor.drone").RemainingHits,Is.EqualTo(1));
        }
        [Test] public void PiercingHitsNearEnemyFirstAndNeverRepeatsSameEnemy()
        {
            GrantModifier("RiflePierce");
            var foes=Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies");
            foes.Add(90,new ArcadeEnemyRuntime{Id=90,Health=100,Position=new Vector2(0,.7f)});
            foes.Add(1,new ArcadeEnemyRuntime{Id=1,Health=100,Position=new Vector2(0,1.8f)});
            Call("Shoot",Vector2.zero,Vector2.up,config.WeaponLevel("Rifle",1),"Rifle");
            Call("AdvanceBullets",.05f);Assert.That(foes[90].Health,Is.EqualTo(64));Assert.That(foes[1].Health,Is.EqualTo(100));
            Call("AdvanceBullets",.01f);Assert.That(foes[90].Health,Is.EqualTo(64));
            Call("AdvanceBullets",.1f);Assert.That(foes[1].Health,Is.EqualTo(64));Assert.That(Get<Dictionary<int,ArcadeProjectileRuntime>>("bullets"),Is.Empty);
        }
        [Test] public void ExplosionExcludesDirectTargetAndOnlyTriggersOnce()
        {
            GrantModifier("RifleExplosion");GrantModifier("RiflePierce");
            var foes=Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies");
            foes.Add(90,new ArcadeEnemyRuntime{Id=90,Health=100,Position=new Vector2(0,.7f)});
            foes.Add(91,new ArcadeEnemyRuntime{Id=91,Health=100,Position=new Vector2(1.2f,.7f)});
            foes.Add(92,new ArcadeEnemyRuntime{Id=92,Health=100,Position=new Vector2(0,2.5f)});
            Call("Shoot",Vector2.zero,Vector2.up,config.WeaponLevel("Rifle",1),"Rifle");
            Call("AdvanceBullets",.06f);Assert.That(foes[90].Health,Is.EqualTo(64));Assert.That(foes[91].Health,Is.EqualTo(88));Assert.That(foes[92].Health,Is.EqualTo(100));
            Call("AdvanceBullets",.12f);Assert.That(foes[91].Health,Is.EqualTo(88));
        }
        [Test] public void SplitChildrenDoNotRecurseAndRespectBudget()
        {
            GrantModifier("RifleSplit");GrantModifier("RiflePierce");
            var foes=Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies");foes.Add(90,new ArcadeEnemyRuntime{Id=90,Health=1000,Position=new Vector2(0,.7f)});
            Call("Shoot",Vector2.zero,Vector2.up,config.WeaponLevel("Rifle",1),"Rifle");Call("AdvanceBullets",.06f);
            var shots=Get<Dictionary<int,ArcadeProjectileRuntime>>("bullets");
            Assert.That(shots.Count,Is.EqualTo(3));var children=shots.Values.Where(b=>b.Damage<36).ToArray();Assert.That(children.Length,Is.EqualTo(2));
            foreach(var child in children){Assert.That(child.RemainingHits,Is.EqualTo(1));Assert.That(child.HitEnemyIds,Does.Contain(90));}
            foes.Clear();
            foreach(var child in children){int id=100+child.Id;foes.Add(id,new ArcadeEnemyRuntime{Id=id,Health=100,Position=child.Position+child.Velocity.normalized*.5f});}
            Call("AdvanceBullets",.06f);
            Assert.That(shots.Count,Is.LessThanOrEqualTo(1),"子弹不得继续分裂");
            system.Stop();Assert.That(shots,Is.Empty);
        }
        [Test] public void ModifierBudgetsPreservePrimaryFireAndBlastDamage()
        {
            GrantModifier("RifleSplit");
            var foes=Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies");
            foes.Add(90,new ArcadeEnemyRuntime{Id=90,Health=100000,Position=new Vector2(0,.7f)});
            for(int i=0;i<30;i++){Call("Shoot",Vector2.zero,Vector2.up,config.WeaponLevel("Rifle",1),"Rifle");Call("AdvanceBullets",.001f);}
            var shots=Get<Dictionary<int,ArcadeProjectileRuntime>>("bullets");
            Assert.That(shots.Count,Is.EqualTo(config.MaxSplitProjectiles));
            while(shots.Count<config.MaxProjectiles)Call("Shoot",new Vector2(8,8),Vector2.up,config.WeaponLevel("Rifle",1),"Rifle");
            Assert.That(Call("CanFirePrimary"),Is.True);Assert.That(shots.Count,Is.EqualTo(config.MaxProjectiles-1));
            // 两个分支互斥，另起独立战斗验证爆炸预算，避免旧分裂弹继续命中干扰范围伤害断言。
            Cleanup();Setup();GrantModifier("RifleExplosion");
            foes=Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies");
            foes.Add(90,new ArcadeEnemyRuntime{Id=90,Health=100000,Position=new Vector2(0,.7f)});
            foes.Add(91,new ArcadeEnemyRuntime{Id=91,Health=100000,Position=new Vector2(1.2f,.7f)});
            for(int i=0;i<30;i++){Call("Shoot",Vector2.zero,Vector2.up,config.WeaponLevel("Rifle",1),"Rifle");Call("AdvanceBullets",.001f);}
            Assert.That(Get<System.Collections.ICollection>("modifierBlasts").Count,Is.EqualTo(config.MaxBlastEffects));
            Assert.That(foes[91].Health,Is.EqualTo(100000-30*12),"视觉预算满后伤害不能丢");
            system.Stop();Assert.That(Get<System.Collections.ICollection>("modifierBlasts").Count,Is.Zero);
        }
        [Test] public void FullBudgetWithoutValidShotKeepsChildren()
        {
            GrantModifier("RifleSplit");
            var foes=Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies");
            foes.Add(90,new ArcadeEnemyRuntime{Id=90,Health=100000,Position=new Vector2(0,.7f)});
            for(int i=0;i<20;i++){Call("Shoot",Vector2.zero,Vector2.up,config.WeaponLevel("Rifle",1),"Rifle");Call("AdvanceBullets",.001f);}
            var shots=Get<Dictionary<int,ArcadeProjectileRuntime>>("bullets");
            while(shots.Count<config.MaxProjectiles)Call("Shoot",new Vector2(8,8),Vector2.up,config.WeaponLevel("Rifle",1),"Rifle");
            int children=Get<int>("splitShotCount");foes.Clear();Call("Fire");
            Assert.That(shots.Count,Is.EqualTo(config.MaxProjectiles));Assert.That(Get<int>("splitShotCount"),Is.EqualTo(children));
            foes.Add(90,new ArcadeEnemyRuntime{Id=90,Health=100000,Position=new Vector2(0,.7f)});Set("spatialFrameReady",false);
            model.RifleReloadRemaining=2;model.RifleRounds=0;Call("Fire");
            Assert.That(Get<int>("splitShotCount"),Is.EqualTo(children));
            model.RifleReloadRemaining=0;model.RifleRounds=1;Call("Fire");
            Assert.That(shots.Count,Is.EqualTo(config.MaxProjectiles));Assert.That(Get<int>("splitShotCount"),Is.EqualTo(children-1));
        }
        [Test] public void InFlightExplosionAndSplitKeepLaunchLevel()
        {
            foreach(string branch in new[]{"RifleExplosion","RifleSplit"}){
                Cleanup();Setup();GrantModifier(branch);
                Call("Shoot",Vector2.zero,Vector2.up,config.WeaponLevel("Rifle",1),"Rifle");
                GrantModifier(branch);
                var foes=Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies");
                foes.Add(90,new ArcadeEnemyRuntime{Id=90,Health=100,Position=new Vector2(0,.7f)});
                foes.Add(91,new ArcadeEnemyRuntime{Id=91,Health=100,Position=new Vector2(1.2f,.7f)});
                Call("AdvanceBullets",.06f);
                Assert.That(foes[91].Health,Is.EqualTo(branch=="RifleExplosion"?88:100));
                var shots=Get<Dictionary<int,ArcadeProjectileRuntime>>("bullets");
                Assert.That(shots.Count,Is.EqualTo(branch=="RifleSplit"?2:0));
                Assert.That(shots.Values.All(b=>b.Damage==12),Is.True);
            }
        }
        [Test] public void ModifierShotsAndBlastRespectSolidObstacles()
        {
            GrantModifier("RiflePierce");GrantModifier("RifleExplosion");
            var foes=Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies");
            foes.Add(90,new ArcadeEnemyRuntime{Id=90,Health=100,Position=new Vector2(0,2.5f)});
            Set("obstacles",new[]{new ArcadeObstacleDefinition(0,.2f,10,.05f,true)});
            Call("Shoot",Vector2.zero,Vector2.up,config.WeaponLevel("Rifle",1),"Rifle");Call("AdvanceBullets",.25f);
            Assert.That(foes[90].Health,Is.EqualTo(100));Assert.That(Get<Dictionary<int,ArcadeProjectileRuntime>>("bullets"),Is.Empty);
            foes.Clear();foes.Add(90,new ArcadeEnemyRuntime{Id=90,Health=100,Position=new Vector2(0,.7f)});
            foes.Add(91,new ArcadeEnemyRuntime{Id=91,Health=100,Position=new Vector2(1.2f,.7f)});
            Set("obstacles",new[]{new ArcadeObstacleDefinition(.6f,.7f,.05f,3,true)});
            Call("Shoot",Vector2.zero,Vector2.up,config.WeaponLevel("Rifle",1),"Rifle");Call("AdvanceBullets",.06f);
            Assert.That(foes[91].Health,Is.EqualTo(100),"范围伤害不能穿实体障碍");
        }
        [Test] public void SimultaneousModifierShotsCommitOneKillAndDrop()
        {
            GrantModifier("RifleExplosion");GrantModifier("RiflePierce");
            var foes=Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies");
            foes.Add(90,new ArcadeEnemyRuntime{Id=90,Health=1,MaxHealth=1,Position=new Vector2(0,.7f)});
            for(int i=0;i<4;i++)Call("Shoot",Vector2.zero,Vector2.up,config.WeaponLevel("Rifle",1),"Rifle");
            Call("AdvanceBullets",.06f);
            Assert.That(model.Kills,Is.EqualTo(1));
            Assert.That(Get<Dictionary<int,(Vector2 Position,long Experience)>>("drops").Values.Sum(x=>x.Experience),Is.EqualTo(config.ExperiencePerKill));
        }
        /// <summary>无人机不接受弹丸强化，历史等级也不应影响新发射的子弹。</summary>
        [Test] public void DroneRejectsAllProjectileModifiersAndIgnoresLegacyLevels()
        {
            model.Loadout.TryUpgrade("Drone",5);
            foreach(var effect in new[]{"Pierce","Explosion","Split"}){
                int id=ModifierId("Drone"+effect);Assert.That(config.Upgrades[id-1].Weight,Is.Zero);
                model.State.AddExperience(model.State.RequiredExperience);model.State.OfferWeighted(new[]{id},new[]{1},1,0);
                Assert.That(system.Choose(model.State.ChoiceRevision,id),Is.False);
                // 人工模拟旧等级：真实发射也不得继续携带无人机弹丸强化。
                Assert.That(model.State.Choose(model.State.ChoiceRevision,id),Is.True);
            }
            model.State.AddExperience(model.State.RequiredExperience);Call("Offer");
            Assert.That(model.UpgradeChoices.Any(id=>config.Upgrades[id-1].IsModifier&&config.Upgrades[id-1].TargetWeapon=="Drone"),Is.False);
            Call("Shoot",Vector2.zero,Vector2.up,config.WeaponLevel("Drone",1),"Drone");
            var shot=Get<Dictionary<int,ArcadeProjectileRuntime>>("bullets").Values.Single();
            Assert.That(shot.RemainingHits,Is.EqualTo(1));
            var snapshot=Get<System.Collections.IDictionary>("modifierShots")[shot.Id];var type=snapshot.GetType();
            Assert.That(type.GetField("SplitCount").GetValue(snapshot),Is.EqualTo(0));
            Assert.That(type.GetField("BlastDamage").GetValue(snapshot),Is.EqualTo(0));
        }
        /// <summary>每级只增加一条步枪弹道，每颗扣一发；无人机仍为单弹。</summary>
        [Test] public void RifleMultishotAddsOneLanePerLevelAndConsumesMatchingRounds()
        {
            var foes=Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies");
            foes.Add(90,new ArcadeEnemyRuntime{Id=90,Health=1000,Position=new Vector2(0,3)});
            var shots=Get<Dictionary<int,ArcadeProjectileRuntime>>("bullets");
            for(int level=1;level<=4;level++){
                GrantModifier("RifleMultishot");shots.Clear();Set("fireClock",0f);
                model.RifleCapacity=30;model.RifleRounds=30;model.RifleReloadRemaining=0;
                Call("Fire");Assert.That(shots.Count,Is.EqualTo(level+1));
                Assert.That(model.RifleRounds,Is.EqualTo(30-level-1));
                Assert.That(shots.Values.All(b=>b.Damage==config.WeaponLevel("Rifle",1).Damage),Is.True);
                Assert.That(shots.Values.Sum(b=>b.Velocity.x),Is.EqualTo(0).Within(.001));
                var angles=shots.Values.Select(b=>Mathf.Atan2(b.Velocity.x,b.Velocity.y)*Mathf.Rad2Deg).OrderBy(a=>a).ToArray();
                for(int i=1;i<angles.Length;i++)Assert.That(angles[i]-angles[i-1],Is.EqualTo(6).Within(.001));
            }
            Assert.That(config.Upgrades.Any(u=>u.Kind=="DroneMultishot"),Is.False);
            shots.Clear();Call("Shoot",Vector2.zero,Vector2.up,config.WeaponLevel("Drone",1),"Drone");
            Assert.That(shots.Count,Is.EqualTo(1));
            model.State.AddExperience(model.State.RequiredExperience);Call("Offer");
            Assert.That(model.UpgradeChoices,Has.No.Member(ModifierId("RifleMultishot")));
        }
        /// <summary>预算只剩一个位置仍能发射，且不会超额或额外扣弹。</summary>
        [Test] public void MultishotRespectsCapacityAndCopiesPiercingToEachLane()
        {
            for(int i=0;i<4;i++)GrantModifier("RifleMultishot");
            GrantModifier("RiflePierce");GrantModifier("RifleExplosion");
            var foes=Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies");
            foes.Add(90,new ArcadeEnemyRuntime{Id=90,Health=1000,Position=new Vector2(0,3)});
            var shots=Get<Dictionary<int,ArcadeProjectileRuntime>>("bullets");
            Call("Fire");Assert.That(shots.Count,Is.EqualTo(5));
            Assert.That(shots.Values.All(b=>b.RemainingHits==2),Is.True);
            var snapshots=Get<System.Collections.IDictionary>("modifierShots");
            foreach(var shot in shots.Values){
                var snapshot=snapshots[shot.Id];var type=snapshot.GetType();
                Assert.That(type.GetField("SplitCount").GetValue(snapshot),Is.EqualTo(0));
                Assert.That(type.GetField("BlastDamage").GetValue(snapshot),Is.EqualTo(12));
            }
            shots.Clear();
            while(shots.Count<config.MaxProjectiles-1)Call("Shoot",new Vector2(10,10),Vector2.up,config.WeaponLevel("Rifle",1),"Rifle");
            int ammo=model.RifleRounds;Set("fireClock",0f);Call("Fire");
            Assert.That(shots.Count,Is.EqualTo(config.MaxProjectiles));
            Assert.That(model.RifleRounds,Is.EqualTo(ammo-1));
            var last=shots.Values.OrderByDescending(b=>b.Id).First();
            Assert.That(last.Velocity.x,Is.EqualTo(0).Within(.001));
            Set("fireClock",0f);Call("Fire");Assert.That(model.RifleRounds,Is.EqualTo(ammo-1));
        }
        /// <summary>双向分支互斥不影响继续升级或叠加穿透、多重；旧请求也必须被拒绝。</summary>
        [Test] public void BodyBranchLocksRivalButKeepsChosenUpgrades()
        {
            foreach(var chosen in new[]{"RifleExplosion","RifleSplit"}){
                model.State=new SurvivorRunState(10,5);
                string rival=chosen=="RifleExplosion"?"RifleSplit":"RifleExplosion";
                GrantModifier(chosen);
                int rivalId=ModifierId(rival);
                Assert.That(Call("CanChooseModifier",config.Upgrades[rivalId-1],rivalId),Is.False);
                GrantModifier(chosen);GrantModifier(chosen);
                GrantModifier("RiflePierce");GrantModifier("RifleMultishot");
                model.State.AddExperience(model.State.RequiredExperience);Call("Offer");
                Assert.That(model.UpgradeChoices,Has.No.Member(rivalId));
                model.State.OfferWeighted(new[]{rivalId},new[]{1},1,0);
                Assert.That(system.Choose(model.State.ChoiceRevision,rivalId),Is.False);
                int id=ModifierId(chosen);Assert.That(Call("CanChooseModifier",config.Upgrades[id-1],id),Is.False);
            }
        }
        /// <summary>齐射数量受剩余弹药约束，每颗扣一发，打空后正常换弹。</summary>
        [Test] public void MultishotConsumesActualRoundsAndReloadsAfterPartialVolley()
        {
            for(int i=0;i<4;i++)GrantModifier("RifleMultishot");
            var foes=Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies");
            foes.Add(90,new ArcadeEnemyRuntime{Id=90,Health=1000,Position=new Vector2(0,3)});
            var shots=Get<Dictionary<int,ArcadeProjectileRuntime>>("bullets");
            for(int rounds=1;rounds<=5;rounds++){
                shots.Clear();Set("fireClock",0f);model.RifleRounds=rounds;model.RifleCapacity=30;model.RifleReloadRemaining=0;
                Call("Fire");Assert.That(shots.Count,Is.EqualTo(rounds));
                Assert.That(model.RifleRounds,Is.Zero);Assert.That(model.RifleReloadRemaining,Is.GreaterThan(0));
                Set("fireClock",0f);Call("Fire");Assert.That(shots.Count,Is.EqualTo(rounds));
            }
        }
        private int ModifierId(string kind)
        {
            int index=config.Upgrades.ToList().FindIndex(x=>x.Kind==kind);
            Assert.That(index,Is.GreaterThanOrEqualTo(0),"缺少正式强化："+kind);return index+1;
        }
        private void GrantModifier(string kind)
        {
            int id=ModifierId(kind);model.State.AddExperience(model.State.RequiredExperience);
            model.State.OfferWeighted(new[]{id},new[]{1},1,0);
            Assert.That(system.Choose(model.State.ChoiceRevision,id),Is.True);
        }
        [SetUp] public void Setup()
        {
            architecture=new Game();model=new SurvivorModel();arcade=new ArcadeRunModel();architecture.RegisterModel(model);architecture.RegisterModel(arcade);architecture.RegisterModel(new MapSceneModel());
            var provider=new LubanRuntimeConfigProvider(()=>new Tables(name=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",name+".bytes")))));
            architecture.RegisterUtility<IRuntimeConfigProvider>(provider);
            Assert.That(provider.TryGetSurvivor(out config,out var error),Is.True,error);Assert.That(provider.TryGetArcadeCatalog(out var catalog,out error),Is.True,error);Assert.That(provider.TryGetEnemy(config.EnemyId,out var enemy,out error),Is.True,error);
            system=new SurvivorSystem();((ICanSetArchitecture)system).SetArchitecture(architecture);view=new TestView();input=new PlayerInputCtrl();root=Path.Combine(Path.GetTempPath(),"SurvivorRuntimeTests-"+Guid.NewGuid().ToString("N"));
            Set("config",config);Set("catalog",catalog);Set("layout",config.Layout);Set("weapon",catalog.Weapons.Single(x=>x.Id==config.WeaponId));Set("enemyConfig",enemy);Set("obstacles",Array.Empty<ArcadeObstacleDefinition>());Set("view",view);Set("input",input);Set("store",new SurvivorResultFileStore(1,root));Set("runId",123L);
            model.State=new SurvivorRunState(config.InitialExperienceThreshold,config.ExperienceThresholdGrowth);model.Loadout=new SurvivorLoadout(config.WeaponSlots,"Rifle");model.Active=true;model.Health=model.MaximumHealth=config.PlayerMaxHealth;
            arcade.CommitSafeNode(new ArcadeRunSaveData{RunId="unrelated-arcade",RemainingLives=3,NextArenaNumber=1});
        }
        [TearDown] public void Cleanup(){system.Stop();input.Dispose();if(Directory.Exists(root))Directory.Delete(root,true);}
        /// <summary>已开局的账本不可替换时保留现场金币，恢复可写后冷却重试只提交一次。</summary>
        [Test] public void GoldWriteFailureKeepsDropUntilCooldownRetrySucceeds()
        {
            var gold=new SurvivorGoldFileStore(1,root);Set("goldStore",gold);
            Assert.That(typeof(SurvivorSystem).GetMethod("BeginGoldRun",Flags).Invoke(system,new object[]{null}),Is.True);
            var coins=Get<Dictionary<int,Vector2>>("goldDrops");coins.Add(701,Vector2.zero);Set("player",Vector2.zero);
            var path=Path.Combine(root,"Saves","survivor-gold-1.json");var previous=File.ReadAllText(path);
            try
            {
                File.SetAttributes(path,FileAttributes.ReadOnly);
                Assert.That(gold.TryCollect(Get<string>("goldRunToken")+":1",out _,out var error),Is.False,"必须制造实际落盘失败");
                // 同一错误已经提示过，避免本测试启动无关的浮动提示资源加载。
                typeof(SurvivorSystem).GetProperty("LastError").SetValue(system,error);
                Call("TickGold",.1f);
                Assert.That(coins.Count,Is.EqualTo(1));Assert.That(view.RemovedDrops,Is.Zero);
                Assert.That(model.CollectedGold,Is.Zero);Assert.That(model.TotalGold,Is.Zero);Assert.That(view.GoldFeedbacks,Is.Zero,"失败不能播放成功反馈");
                Assert.That(File.ReadAllText(path),Is.EqualTo(previous));
            }
            finally { File.SetAttributes(path,FileAttributes.Normal); }
            Call("TickGold",1f);Assert.That(coins.Count,Is.EqualTo(1));Assert.That(view.RemovedDrops,Is.Zero);
            Call("TickGold",1.1f);Call("TickGold",.1f);
            Assert.That(coins,Is.Empty);Assert.That(view.RemovedDrops,Is.EqualTo(1));Assert.That(view.GoldFeedbacks,Is.EqualTo(1),"重试成功仅反馈一次");
            Assert.That(model.CollectedGold,Is.EqualTo(1));Assert.That(model.TotalGold,Is.EqualTo(1));
            Assert.That(gold.TryRead(out var balance,out var readError),Is.True,readError);Assert.That(balance,Is.EqualTo(1));
        }
        /// <summary>真实金币拾取落盘后才移除；重复Tick不能重复入账，重开保留余额。</summary>
        [Test] public void GoldPickupPersistsBeforeRemovalAndNewRunKeepsBalance()
        {
            Set("goldStore",new SurvivorGoldFileStore(1,root));
            var begin=typeof(SurvivorSystem).GetMethod("BeginGoldRun",Flags);
            Assert.That(begin.Invoke(system,new object[]{null}),Is.True);
            var coins=Get<Dictionary<int,Vector2>>("goldDrops");coins.Add(700,Vector2.zero);
            Set("player",Vector2.zero);Call("TickGold",.1f);Call("TickGold",.1f);
            Assert.That(view.GoldFeedbacks,Is.EqualTo(1),"成功领取必须反馈且不能重播");Assert.That(view.LastGoldPosition,Is.EqualTo(Vector2.zero));
            Assert.That(coins,Is.Empty);Assert.That(model.CollectedGold,Is.EqualTo(1));Assert.That(model.TotalGold,Is.EqualTo(1));
            Assert.That(new SurvivorGoldFileStore(1,root).TryRead(out var balance,out var error),Is.True,error);Assert.That(balance,Is.EqualTo(1));
            Assert.That(begin.Invoke(system,new object[]{null}),Is.True);Assert.That(model.CollectedGold,Is.Zero);Assert.That(model.TotalGold,Is.EqualTo(1));
        }
        /// <summary>步枪打空30发后停火，换弹完成才重新发射，备弹不限制循环。</summary>
        [Test] public void RifleWaitsForAutomaticReloadAfterThirtyRounds()
        {
            Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies").Add(901,new ArcadeEnemyRuntime{Id=901,Health=100,Position=new Vector2(2,0)});
            var shots=Get<Dictionary<int,ArcadeProjectileRuntime>>("bullets");
            for(int i=0;i<31;i++){Set("fireClock",0f);Call("Fire");}
            Assert.That(shots.Count,Is.EqualTo(30),"空弹夹必须停火");
            Call("AdvanceRifleReload",1f);Set("fireClock",0f);Call("Fire");Assert.That(shots.Count,Is.EqualTo(30));
            Call("AdvanceRifleReload",1f);Set("fireClock",0f);Call("Fire");Assert.That(shots.Count,Is.EqualTo(31));
        }
        /// <summary>通过真实选择事务取得指定强化，避免直接改内部层数。</summary>
        private int SelectMagazineUpgrade(string kind)
        {
            int id=Enumerable.Range(1,config.Upgrades.Count).Single(i=>config.Upgrades[i-1].Kind==kind);
            model.State.AddExperience(model.State.RequiredExperience);model.State.Offer(new[]{id,1,2},17);
            Assert.That(system.Choose(model.State.ChoiceRevision,id),Is.True);return id;
        }
        [Test] public void MagazineUpgradeAddsThreeRoundsWithoutRefillingCurrentMagazine()
        {
            Call("EnsureRifleMagazine");for(int i=0;i<8;i++)Assert.That(Call("TryConsumeRifleRound"),Is.True);
            int id=SelectMagazineUpgrade("Magazine");SelectMagazineUpgrade("Magazine");
            Assert.That(model.RifleAmmo,Is.EqualTo(22));Assert.That(model.RifleMagazineSize,Is.EqualTo(36));
            Assert.That(system.TryGetRifleUpgradeDetails(id,out var magazine,out var current,out var next),Is.True);
            Assert.That(magazine,Is.True);Assert.That(current,Is.EqualTo(36));Assert.That(next,Is.EqualTo(39));
            for(int i=0;i<22;i++)Call("TryConsumeRifleRound");Call("AdvanceRifleReload",2f);Assert.That(model.RifleAmmo,Is.EqualTo(36));
        }
        [Test] public void ReloadUpgradeKeepsActiveTimerAndStopsAtOneSecond()
        {
            for(int i=0;i<30;i++)Call("TryConsumeRifleRound");Call("AdvanceRifleReload",.5f);
            int id=SelectMagazineUpgrade("Reload");Assert.That(model.RifleReloadTimeRemaining,Is.EqualTo(1.5f));
            for(int i=0;i<4;i++)SelectMagazineUpgrade("Reload");
            Assert.That(system.TryGetRifleUpgradeDetails(id,out _,out var current,out _),Is.True);Assert.That(current,Is.EqualTo(1));
            Call("AdvanceRifleReload",1.5f);for(int i=0;i<30;i++)Call("TryConsumeRifleRound");Assert.That(model.RifleReloadTimeRemaining,Is.EqualTo(1f));
            model.State.AddExperience(model.State.RequiredExperience);Call("Offer");Assert.That(model.UpgradeChoices,Has.No.Member(id));
        }
        [Test] public void ReloadFreezesDuringPauseAndResetsForNewRun()
        {
            UIHelper.ResetForTests();architecture.RegisterModel(new AppStateModel());architecture.GetModel<AppStateModel>().CurrentState.Value=AppState.Playing;
            Set("navigation",new SurvivorNavigation(30.5f,30.5f,.5f,Array.Empty<ArcadeObstacleDefinition>()));Set("spawnClock",100f);
            for(int i=0;i<30;i++)Call("TryConsumeRifleRound");system.SetPaused(true);Call("TickFrame",.05f);Assert.That(model.RifleReloadTimeRemaining,Is.EqualTo(2f));
            system.SetPaused(false);Call("TickFrame",.05f);Assert.That(model.RifleReloadTimeRemaining,Is.LessThan(2f));
            system.Stop();Assert.That(model.RifleAmmo,Is.Zero);Assert.That(model.RifleReloadTimeRemaining,Is.Zero);
            model.State=new SurvivorRunState(10,5);Call("EnsureRifleMagazine");Assert.That(model.RifleAmmo,Is.EqualTo(30));
        }
        [Test] public void MissingTargetAndProjectileBudgetDoNotConsumeAmmoAndDroneKeepsFiring()
        {
            Call("EnsureRifleMagazine");Call("Fire");Assert.That(model.RifleAmmo,Is.EqualTo(30));
            Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies").Add(901,new ArcadeEnemyRuntime{Id=901,Health=100,Position=new Vector2(2,0)});
            var shots=Get<Dictionary<int,ArcadeProjectileRuntime>>("bullets");for(int i=0;i<config.MaxProjectiles;i++)shots.Add(1000+i,new ArcadeProjectileRuntime());
            Call("Fire");Assert.That(model.RifleAmmo,Is.EqualTo(30));shots.Clear();
            for(int i=0;i<30;i++)Call("TryConsumeRifleRound");model.Loadout.TryUpgrade("Drone",5);Call("TickDrone",.01f);
            Assert.That(shots.Values.Single().SourceWeaponId,Is.EqualTo("survivor.drone"));Assert.That(model.RifleAmmo,Is.Zero);
        }
        /// <summary>拾取卡展示包含基础半径的真实数值，后续升级持续累加。</summary>
        [Test] public void PickupUpgradeDetailsIncludeBaseRadiusAndCurrentLevels()
        {
            int id=Array.FindIndex(config.Upgrades.ToArray(),x=>x.Kind=="Pickup")+1;
            var method=typeof(SurvivorSystem).GetMethod("TryGetPickupUpgradeDetails");Assert.That(method,Is.Not.Null);
            for(int level=0;level<12;level++)
            {
                object[] args={id,0d,0d};Assert.That(method.Invoke(system,args),Is.True);
                Assert.That((double)args[1],Is.EqualTo(2+level*.25));Assert.That((double)args[2],Is.EqualTo(2+(level+1)*.25));
                model.State.AddExperience(model.State.RequiredExperience);
                model.State.Offer(new[]{id,1,2},17);Assert.That(model.State.Choose(model.State.ChoiceRevision,id),Is.True);
            }
            object[] other={1,0d,0d};Assert.That(method.Invoke(system,other),Is.False);
        }
        /// <summary>幸存者敌人持续累积，超过旧的存活阈值仍按刷新节奏生成。</summary>
        [Test] public void SpawnContinuesBeyondFormerAliveLimit()
        {
            var enemies=Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies");
            for(int i=0;i<125;i++)Call("Spawn");
            Assert.That(enemies.Count,Is.EqualTo(125),"不能因存活数量达到60而停止刷新");
            Assert.That(model.Paused,Is.False);
        }
        [Test] public void GroundSpawnWithNoWalkableFloorWaitsWithoutFailingRun()
        {
            Set("obstacles",new[]{new ArcadeObstacleDefinition(0,0,100,100,true)});
            Call("Spawn");
            Assert.That(Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies"),Is.Empty);
            Assert.That(model.Paused,Is.False);Assert.That(system.LastError,Is.Null);
            Assert.That(model.ResultSnapshot,Is.Null,"配置故障不能伪造失败结算");
            Call("Spawn");Assert.That(Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies"),Is.Empty);
        }
        [TestCase(false)] [TestCase(true)]
        public void SpawnCameraCheckIncludesBodyAndScreenMargin(bool orthographic)
        {
            var cameraObject=new GameObject("SpawnCameraTest");var map=new GameObject("SpawnMapTest");
            try
            {
                var camera=cameraObject.AddComponent<Camera>();camera.enabled=true;camera.orthographic=orthographic;
                camera.orthographicSize=10;camera.fieldOfView=90;camera.aspect=1;
                camera.transform.position=new Vector3(0,10,0);camera.transform.rotation=Quaternion.Euler(90,0,0);
                var method=typeof(ArcadeCombatViewController).GetMethod("IsSpawnHiddenByCamera",BindingFlags.Static|BindingFlags.NonPublic);
                bool Hidden(Vector2 p)=>(bool)method.Invoke(null,new object[]{camera,map.transform,p,.5f,new Plane[6]});
                Assert.That(Hidden(Vector2.zero),Is.False);
                Assert.That(Hidden(new Vector2(10.6f,0)),Is.False,"画面边缘外仍保留身体与镜头移动余量");
                Assert.That(Hidden(new Vector2(20,0)),Is.True);
                map.transform.position=Vector3.right*40;Assert.That(Hidden(Vector2.zero),Is.True,"应使用正式地图世界变换");
                Assert.That((bool)method.Invoke(null,new object[]{null,map.transform,Vector2.zero,.5f,new Plane[6]}),Is.False);
            }
            finally{UnityEngine.Object.DestroyImmediate(cameraObject);UnityEngine.Object.DestroyImmediate(map);}
        }
        [Test] public void HiddenLaneExtensionRejectsWallBeyondOriginalSpawn()
        {
            var layout=Get<ArcadeRoundLayoutParameters>("layout");
            var stage=Get<ArcadeConfigCatalogDefinition>("catalog").EarlyStages.Single(x=>x.ArenaConfigNumber==layout.ArenaConfigNumber);
            Assert.That(ArcadeOutsideRouteRules.TryStart(layout,0,stage.HalfWidth,stage.HalfHeight,.5f,out var entry),Is.True);
            var dx=(entry.StartX-entry.TargetX)/entry.Distance;var dz=(entry.StartZ-entry.TargetZ)/entry.Distance;
            var clone=(ArcadeRoundLayoutParameters)typeof(object).GetMethod("MemberwiseClone",Flags).Invoke(layout,null);
            var wall=new ArcadeLayoutObstacle(entry.StartX+dx*3,entry.StartZ+dz*3,1,1);
            typeof(ArcadeRoundLayoutParameters).GetField("<Obstacles>k__BackingField",Flags).SetValue(clone,layout.Obstacles.Concat(new[]{wall}).ToArray());
            Assert.That(ArcadeOutsideRouteRules.TryExtendToHidden(clone,entry,.5f,(x,z)=>Mathf.Abs(x)>60||Mathf.Abs(z)>60,out _),Is.False);
        }
        [Test] public void GroundSpawnKeepsEnemyInsideFarmEvenWhenCameraSeesIt()
        {
            view.SpawnHidden=p=>Mathf.Abs(p.x)>45||Mathf.Abs(p.y)>45;
            Call("Spawn");var enemy=Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies").Values.Single();
            Assert.That(view.SpawnHidden(enemy.Position),Is.False,"场内可见落点应通过爬出过程自然登场");
            Assert.That(enemy.OutsideEntry.Active,Is.False);Assert.That(enemy.GroundEntry.Active,Is.True);
            var state=enemy.GroundEntry;Assert.That(SurvivorGroundEntryRules.Advance(ref state,.1f),Is.True);
            Assert.That(state.Age,Is.EqualTo(.1f));Assert.That(SurvivorGroundEntryRules.Position(state),Is.EqualTo(enemy.Position),"翻土提示期不移动");
        }
        [Test] public void VisibleGroundSpawnDoesNotNeedHiddenOutsideLane()
        {
            view.SpawnHidden=p=>false;Call("Spawn");
            Assert.That(Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies").Count,Is.EqualTo(1));
            Assert.That(model.Paused,Is.False);Assert.That(system.LastError,Is.Null);
        }
        [Test] public void GroundSpawnPauseAndStandingKillLandExperienceInsideFarm()
        {
            Call("Spawn");var enemies=Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies");var enemy=enemies.Values.Single();
            Assert.That(enemy.GroundEntry.Active,Is.True);Assert.That(Mathf.Abs(enemy.Position.x),Is.LessThan(30.5f));
            var start=enemy.Position;system.SetPaused(true);Call("Tick");Assert.That(enemy.Position,Is.EqualTo(start));
            var bullets=Get<Dictionary<int,ArcadeProjectileRuntime>>("bullets");
            enemy.GroundEntry.Age=SurvivorGroundEntryRules.TotalSeconds;
            bullets.Add(500,new ArcadeProjectileRuntime{Id=500,Position=start-Vector2.right,Velocity=Vector2.right*10,Damage=enemy.Health,RemainingSeconds=1});
            Call("AdvanceBullets",.1f);Assert.That(enemies,Is.Empty);Assert.That(model.Kills,Is.EqualTo(1));
            var drops=Get<Dictionary<int,(Vector2 Position,long Experience)>>("drops");
            Assert.That(drops.Values.Single().Position,Is.EqualTo(start));
        }
        [Test] public void MovementTickDoesNotClearAnimationWithSecondMove()
        {
            UIHelper.ResetForTests();
            architecture.RegisterModel(new AppStateModel());
            architecture.GetModel<AppStateModel>().CurrentState.Value=AppState.Playing;
            Set("navigation",new SurvivorNavigation(30.5f,30.5f,.5f,Array.Empty<ArcadeObstacleDefinition>()));
            Set("spawnClock",100f);
            typeof(PlayerInputCtrl).GetProperty("MoveInput").SetValue(input,Vector2.right);
            Call("TickFrame",.02f);
            Assert.That(Get<Vector2>("player").x,Is.GreaterThan(0));
            Assert.That(view.PlayerWrites,Is.EqualTo(1),"同帧第二次零位移会清空腿部动画速度");
            Assert.That(view.CombatPoseWrites,Is.EqualTo(1),"末尾仍需刷新本帧射击朝向和保护状态");
        }
        [Test] public void PauseFreezesClockEnemiesAndInputPresentation()
        {
            system.SetPaused(true);var before=Get<Vector2>("player");Call("Tick");Assert.That(model.State.ElapsedSeconds,Is.Zero);Assert.That(Get<Vector2>("player"),Is.EqualTo(before));Assert.That(view.PlayerWrites,Is.Zero);Assert.That(Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies"),Is.Empty);
        }
        [Test] public void LostPreparedBattleReportsFailureOnceInsteadOfSilentlyFreezing()
        {
            var commit=typeof(SurvivorSystem).GetMethod("ConfirmBattlePrepared");
            Assert.That(commit,Is.Not.Null,"缺少准备完成交接");
            commit.Invoke(system,null);
            int changes=0;architecture.RegisterEvent<SurvivorChangedEvent>(_=>changes++);
            view.Ready=false;
            Call("Tick");Call("Tick");
            Assert.That(model.Paused,Is.True);
            Assert.That(system.LastError,Does.Contain("战场"));
            Assert.That(changes,Is.EqualTo(1));
            Assert.That(model.State.ElapsedSeconds,Is.Zero);
            Assert.That(Directory.Exists(root),Is.False,"准备故障不能伪造结算存档");
        }
        [Test] public void InitialPreparationWaitDoesNotReportLostBattle()
        {
            view.Ready=false;Call("Tick");
            Assert.That(system.LastError,Is.Null);
            Assert.That(model.Paused,Is.False);
        }
        [Test] public void UnreadyBattleCannotCommitPreparation()
        {
            view.Ready=false;
            Assert.Throws<InvalidOperationException>(()=>system.ConfirmBattlePrepared());
            Assert.That(model.State.ElapsedSeconds,Is.Zero);
        }
        [Test] public void FarmEntryUsesItsConfiguredArenaBounds()
        {
            var position = new Vector2(-28, -14);
            var resolved = (Vector2)Call("Resolve", position, .5f);
            Assert.That(resolved, Is.EqualTo(position), "不能用旧竞技场的小边界截断草地入口");
        }
        [Test] public void ConsecutiveLevelsRequireSeparateVersionedChoices()
        {
            model.State.AddExperience(config.InitialExperienceThreshold*2L+config.ExperienceThresholdGrowth);Call("Offer");Assert.That(model.State.PendingChoices,Is.EqualTo(2));int revision=model.Snapshot.ChoiceRevision,id=model.UpgradeChoices[0];Assert.That(system.Choose(revision,id),Is.True);Assert.That(model.Paused,Is.True);Assert.That(system.Choose(revision,id),Is.False);Assert.That(system.Choose(model.Snapshot.ChoiceRevision,model.UpgradeChoices[0]),Is.True);Assert.That(model.Paused,Is.False);
        }
        [Test] public void FirstUpgradeAcquiresDroneAndNextChoiceUpgradesSameSlot()
        {
            int id=Enumerable.Range(1,config.Upgrades.Count).Single(i=>config.Upgrades[i-1].Kind=="Drone");
            model.State.AddExperience(config.InitialExperienceThreshold);Call("Offer");Assert.That(model.UpgradeChoices,Does.Contain(id));
            Assert.That(system.Choose(0,id),Is.True);Assert.That(model.WeaponLevel("Drone"),Is.EqualTo(1));Assert.That(model.UsedWeaponSlots,Is.EqualTo(2));
            Assert.That(system.TryGetWeaponUpgrade(id,out var kind,out var level,out var current,out var next),Is.True);Assert.That(level,Is.EqualTo(1));Assert.That(next.Damage,Is.GreaterThan(current.Damage));
            model.State.AddExperience(model.State.RequiredExperience);model.State.OfferWeighted(new[]{id,1,2},new[]{1,1,1},1,id);
            Assert.That(system.Choose(1,id),Is.True);Assert.That(model.WeaponLevel("Drone"),Is.EqualTo(2));Assert.That(model.UsedWeaponSlots,Is.EqualTo(2));Assert.That(system.Choose(1,id),Is.False);
        }
        [Test] public void MaxLevelDroneIsRemovedFromCandidates()
        {
            int id=Enumerable.Range(1,config.Upgrades.Count).Single(i=>config.Upgrades[i-1].Kind=="Drone");
            for(int i=0;i<5;i++)model.Loadout.TryUpgrade("Drone",5);
            model.State.AddExperience(config.InitialExperienceThreshold);Call("Offer");Assert.That(model.UpgradeChoices,Has.No.Member(id));Assert.That(system.Choose(0,id),Is.False);
        }
        [Test] public void NewlyAcquiredDroneStartsBesideCurrentPlayer()
        {
            var player=new Vector2(12,-8);Set("player",player);Set("dronePosition",new Vector2(-20,20));
            int id=Enumerable.Range(1,config.Upgrades.Count).Single(i=>config.Upgrades[i-1].Kind=="Drone");
            model.State.AddExperience(config.InitialExperienceThreshold);Call("Offer");Assert.That(system.Choose(0,id),Is.True);
            var tuning=Get<ArcadeConfigCatalogDefinition>("catalog").Rule.Drone;
            Assert.That(Get<Vector2>("dronePosition"),Is.EqualTo(player+new Vector2(tuning.OffsetX,tuning.OffsetZ)));
        }
        [Test] public void DroneFiresAtPlayerThreatAndSharesProjectileBudget()
        {
            model.Loadout.TryUpgrade("Drone",5);
            var enemies=Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies");
            enemies.Add(201,new ArcadeEnemyRuntime{Id=201,Health=100,Position=new Vector2(0,2)});
            enemies.Add(202,new ArcadeEnemyRuntime{Id=202,Health=100,Position=new Vector2(4,0)});
            Set("dronePosition",new Vector2(4,0));Call("TickDrone",.01f);
            var shots=Get<Dictionary<int,ArcadeProjectileRuntime>>("bullets");Assert.That(shots.Count,Is.EqualTo(1));var shot=shots.Values.Single();Assert.That(shot.SourceWeaponId,Is.EqualTo("survivor.drone"));Assert.That(shot.Velocity.x,Is.LessThan(0));Assert.That(shot.Velocity.y,Is.GreaterThan(0));
            for(int i=shots.Count;i<config.MaxProjectiles;i++)shots.Add(1000+i,new ArcadeProjectileRuntime());
            Set("droneFireClock",0f);Call("TickDrone",.01f);Assert.That(shots.Count,Is.EqualTo(config.MaxProjectiles));
        }
        [Test] public void UpgradeSelectionDoesNotDismissMenuPause()
        {
            model.State.AddExperience(config.InitialExperienceThreshold);Call("Offer");system.SetPaused(true);Assert.That(system.Choose(model.Snapshot.ChoiceRevision,model.UpgradeChoices[0]),Is.True);Assert.That(model.Paused,Is.True);system.SetPaused(false);Assert.That(model.Paused,Is.False);
        }
        [Test] public void HealthUpgradeAddsOnlyNewCapacityWithoutFullHeal()
        {
            int id=Enumerable.Range(1,config.Upgrades.Count).Single(i=>config.Upgrades[i-1].Kind=="Health");model.Health=20;model.State.AddExperience(config.InitialExperienceThreshold);var eligible=Enumerable.Range(1,config.Upgrades.Count).ToArray();int seed=0;
            while(true){model.State=new SurvivorRunState(config.InitialExperienceThreshold,config.ExperienceThresholdGrowth);model.State.AddExperience(config.InitialExperienceThreshold);if(model.State.Offer(eligible,seed++).Contains(id))break;Assert.That(seed,Is.LessThan(100));}
            Assert.That(system.Choose(0,id),Is.True);Assert.That(model.Health,Is.EqualTo(20+model.MaximumHealth-config.PlayerMaxHealth));Assert.That(model.Health,Is.LessThan(model.MaximumHealth));
        }
        [Test] public void AutoWeaponsWaitUntilTargetIsVisible()
        {
            Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies").Add(901,new ArcadeEnemyRuntime{Id=901,Health=100,MaxHealth=100,Position=new Vector2(2,0)});
            model.Loadout.TryUpgrade("Drone",5);
            Set("droneVisual",new ArcadePowerupDefinition("survivor.drone",global::ArcadePowerupBehavior.Drone,"",false,Vector3.zero,0,0,0,0,0,0));
            var bullets=Get<Dictionary<int,ArcadeProjectileRuntime>>("bullets");
            view.TargetVisible=false;Call("Fire");Call("TickDrone",.01f);
            Assert.That(bullets,Is.Empty,"看不到敌人时两种自动武器都应等待");
            view.TargetVisible=true;Call("Fire");Call("TickDrone",.01f);
            Assert.That(bullets.Count,Is.EqualTo(2),"进入画面后步枪和无人机都应恢复攻击");
        }
        [Test] public void ExactOverlapKeepsOneDropWithAllExperience()
        {
            KillOne();KillOne();
            var drops=Get<Dictionary<int,(Vector2 Position,long Experience)>>("drops");
            Assert.That(drops.Count,Is.EqualTo(1));
            Assert.That(drops.Values.Single().Experience,Is.EqualTo(config.ExperiencePerKill*2));
            Assert.That(model.State.Experience,Is.Zero,"经验仍需拾取才能获得");
        }
        [Test] public void NearbyDistinctDropsRemainSeparate()
        {
            KillOne();KillOne(new Vector2(1.000001f,0));
            Assert.That(Get<Dictionary<int,(Vector2 Position,long Experience)>>("drops").Count,Is.EqualTo(2),"不能用距离容差合并不同落点");
        }
        [Test] public void ExperiencePresentationSendsNoNameOrQuantityLabel()
        {
            var rootObject=new GameObject("ExperienceNameTest");
            try
            {
                var port=new ExperienceLootSpy();var presentation=new ArcadeCombatViewController();
                typeof(ArcadeCombatViewController).GetField("mapRoot",Flags).SetValue(presentation,rootObject.transform);
                typeof(ArcadeCombatViewController).GetField("lootPresentation",Flags).SetValue(presentation,port);
                presentation.SetExperienceDrop(1,config.ExperienceVisualPrefabAddress,Vector2.zero,3);
                Assert.That(port.Name,Is.Empty);Assert.That(port.Amount,Is.EqualTo(3));
                Assert.That(port.Address,Is.EqualTo(config.ExperienceVisualPrefabAddress));
            }
            finally{UnityEngine.Object.DestroyImmediate(rootObject);}
        }
        private sealed class ExperienceLootSpy:IArcadeLootPresentationPort
        {
            public string Name,Address;public int Amount;
            public bool TrySetDrop(int id,Vector3 position,string name,int amount,out string error){Name=name;Amount=amount;error=null;return true;}
            public bool TrySetDrop(int id,Vector3 position,string name,int amount,string address,out string error){Address=address;return TrySetDrop(id,position,name,amount,out error);}
            public void RemoveDrop(int id){}
            public void ClearArcadeDrops(){}
        }
        private void KillOne(Vector2? position=null)
        {
            var point=position??new Vector2(1,0);
            Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies").Add(901,new ArcadeEnemyRuntime{Id=901,Health=1,MaxHealth=1,Position=point});
            Get<Dictionary<int,ArcadeProjectileRuntime>>("bullets").Add(902,new ArcadeProjectileRuntime{Id=902,Position=point-Vector2.right,Velocity=new Vector2(20,0),Damage=1,RemainingSeconds=1});Call("AdvanceBullets",.05f);
        }
        [Test] public void ZeroExperienceChanceKeepsKillAndScoreWithoutDrop()
        {
            typeof(SurvivorConfiguration).GetProperty("ExperienceDropChance").SetValue(config,0f);
            KillOne();
            Assert.That(model.Kills,Is.EqualTo(1));Assert.That(model.Score,Is.Zero);
            Assert.That(Get<Dictionary<int,(Vector2 Position,long Experience)>>("drops"),Is.Empty);
            Assert.That(model.State.Experience,Is.Zero);
        }
        [Test] public void KillCreatesExperienceOnGroundWithoutRemoteCollection()
        {
            KillOne();Assert.That(model.Kills,Is.EqualTo(1));Assert.That(model.Score,Is.Zero);Assert.That(model.State.Experience,Is.Zero);Assert.That(Get<Dictionary<int,(Vector2 Position,long Experience)>>("drops").Values.Single().Experience,Is.EqualTo(config.ExperiencePerKill));
        }
        [Test] public void FullDropBudgetMergesExperienceWithoutLoss()
        {
            var drops=Get<Dictionary<int,(Vector2 Position,long Experience)>>("drops");for(int i=0;i<config.MaxDrops;i++)drops.Add(1000+i,(new Vector2(i,0),1));KillOne();Assert.That(drops.Count,Is.EqualTo(config.MaxDrops));Assert.That(drops.Values.Sum(x=>x.Experience),Is.EqualTo(config.MaxDrops+config.ExperiencePerKill));
        }
        [Test] public void DeathSavesIndependentResultAndRetryIsIdempotent()
        {
            model.Health=0;model.Kills=2;model.Score=20;Assert.That(system.TryFinish(out var error),Is.True,error);Assert.That(system.TryFinish(out error),Is.True,error);var store=new SurvivorResultFileStore(1,root);Assert.That(store.TryRead(out var result,out error),Is.True,error);Assert.That(result.Outcome,Is.EqualTo("failed"));Assert.That(result.Kills,Is.EqualTo(2));Assert.That(Directory.GetFiles(root,"*",SearchOption.AllDirectories).Length,Is.EqualTo(1));Assert.That(arcade.Current.RunId,Is.EqualTo("unrelated-arcade"));
        }
        [Test] public void QuitSavesAbandonedSurvivorBeforeAllowingApplicationExit()
        {
            architecture.RegisterSystem(system);
            Assert.That(QuitGameCommand.TryPrepareQuit(architecture), Is.True);
            Assert.That(model.ResultSnapshot, Is.Not.Null);
            Assert.That(model.ResultSnapshot.Outcome, Is.EqualTo("abandoned"));
            Assert.That(new SurvivorResultFileStore(1, root).TryRead(out var result, out var error), Is.True, error);
            Assert.That(result, Is.Not.Null);
            Assert.That(arcade.Current.RunId, Is.EqualTo("unrelated-arcade"));
        }
        [Test] public void StopReleasesOwnedViewAndPreservesArcadeRun()
        {
            Set("ownsView",true);system.Stop();system.Stop();Assert.That(view.Ends,Is.EqualTo(1));Assert.That(model.Active,Is.False);Assert.That(arcade.Current.RunId,Is.EqualTo("unrelated-arcade"));Assert.That(Directory.Exists(root),Is.False);
        }
        /// <summary>复用真实配置及敌池，开启可控相机范围供尸潮集成检查。</summary>
        private void PrepareHordeCheck()
        {
            Call("Spawn");Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies").Clear();
            view.HordeEnabled=true;view.Bounds=new Rect(-4,-4,8,8);view.SpawnHidden=p=>!view.Bounds.Contains(p);
            Set("hordeInitialized",true);Set("hordeWaveClock",1000f);
        }
        [Test] public void HordeExpansionUsesFrameBudgetWithoutLosingPopulation()
        {
            PrepareHordeCheck();var state=Get<SurvivorHordeState>("horde");var kind=Get<SurvivorEnemyRoster>("enemyRoster").Entries[0].EnemyId;
            state.Add(0,kind,view.Bounds.xMax+3,0,0,10000);Call("TickHorde",.01f);
            var active=Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies");Assert.AreEqual(config.HordeActivationBudget,active.Count);Assert.AreEqual(10000,state.TotalCount+active.Count);
        }
        [Test] public void HordeWakeFailureDoesNotConsumeGroup()
        {
            PrepareHordeCheck();var state=Get<SurvivorHordeState>("horde");var kind=Get<SurvivorEnemyRoster>("enemyRoster").Entries[0].EnemyId;
            state.Add(0,kind,view.Bounds.xMax+3,0,0,7);view.FailWakeAfter=0;Call("TickHorde",.01f);
            Assert.AreEqual(7,state.TotalCount);Assert.IsEmpty(Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies"));Assert.IsTrue(model.Paused);Assert.IsNotEmpty(system.LastError);
        }
        [Test] public void HordePausedFrameDoesNotAdvanceOrExpand()
        {
            PrepareHordeCheck();var state=Get<SurvivorHordeState>("horde");state.Add(0,config.EnemyId,12,0,2,100);model.Paused=true;
            Call("TickFrame",1f);Assert.AreEqual(100,state.TotalCount);Assert.AreEqual(0,view.Wakes);Assert.AreEqual(1000,Get<float>("hordeWaveClock"));
        }
        [Test] public void HordeStopClearsPendingAndActiveEnemies()
        {
            PrepareHordeCheck();Get<SurvivorHordeState>("horde").Add(0,config.EnemyId,12,0,2,100);Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies").Add(101,new ArcadeEnemyRuntime{Id=101});
            system.Stop();Assert.AreEqual(0,Get<SurvivorHordeState>("horde").TotalCount);Assert.IsEmpty(Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies"));Assert.IsFalse(Get<bool>("hordeInitialized"));
        }
        [Test] public void HordePrewarmIsIncrementalAndDoesNotSpawnAuthority()
        {
            PrepareHordeCheck();bool complete=false;int calls=0;
            while(!complete&&calls<10){int before=view.WarmCalls;Assert.IsTrue(system.TryPrepareHordePresentation(out complete,out var error),error);Assert.LessOrEqual(view.WarmCalls-before,1);calls++;}
            Assert.IsTrue(complete);Assert.IsEmpty(Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies"));Assert.AreEqual(0,Get<SurvivorHordeState>("horde").TotalCount);
        }
        [Test] public void HordePrewarmFailureRemainsAnEntryFailure()
        {
            PrepareHordeCheck();view.WarmFailure=true;Assert.IsFalse(system.TryPrepareHordePresentation(out var complete,out var error));Assert.IsFalse(complete);Assert.IsNotEmpty(error);Assert.AreEqual(0,Get<int>("hordeWarmIndex"));
        }
        [Test] public void ScatterPeripheralAddsOnlyOneAtDifferentPositions()
        {
            PrepareHordeCheck();view.SpawnHidden=p=>true;var state=Get<SurvivorHordeState>("horde");
            Call("SpawnHordeWave");Assert.AreEqual(1,state.TotalCount);
            Call("SpawnHordeWave");Assert.AreEqual(2,state.TotalCount);Assert.AreEqual(2,state.GroupCount);
        }
        [Test] public void PursuitCreatesFarEnemiesWithoutWaitingForCamera()
        {
            PrepareHordeCheck();var state=Get<SurvivorHordeState>("horde");var kind=Get<SurvivorEnemyRoster>("enemyRoster").Entries[0].EnemyId;
            state.Add(0,kind,25,20,2,1);Call("TickHorde",.01f);
            Assert.AreEqual(1,Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies").Count);Assert.AreEqual(0,state.TotalCount);
        }
        [Test] public void PursuitDoesNotRetireFarDamagedEnemy()
        {
            PrepareHordeCheck();var active=Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies");var e=new ArcadeEnemyRuntime{Id=901,ConfigId=config.EnemyId,Position=new Vector2(25,20),Health=23,MaxHealth=100,Speed=2};active.Add(e.Id,e);
            Call("TickHorde",.25f);Assert.AreSame(e,active[901]);Assert.AreEqual(0,view.Suspends);Assert.AreEqual(23,e.Health);
        }
        [Test] public void PursuitFarEnemyMovesTowardPlayerImmediately()
        {
            PrepareHordeCheck();Set("player",Vector2.zero);var state=Get<SurvivorHordeState>("horde");var kind=Get<SurvivorEnemyRoster>("enemyRoster").Entries[0].EnemyId;
            state.Add(0,kind,25,20,2,1);Call("TickHorde",.01f);var active=Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies");Assert.AreEqual(1,active.Count);
            var e=active.Values.Single();float before=e.Position.magnitude;Call("TickEnemies",.25f);Assert.Less(e.Position.magnitude,before);
        }
        [Test] public void PursuitStartsEmptyWithoutPrepopulatedBirthRequests()
        {
            PrepareHordeCheck();Set("hordeInitialized",false);Call("TickHorde",.01f);
            Assert.IsEmpty(Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies"));
            Assert.AreEqual(0,Get<SurvivorHordeState>("horde").TotalCount);Assert.AreEqual(0,view.Wakes);
        }
        [Test] public void PursuitBirthsAccumulateOverTimeWithoutOpeningBurst()
        {
            PrepareHordeCheck();Set("hordeInitialized",false);int previous=0;
            for(int i=0;i<40;i++)
            {
                Call("TickHorde",.05f);int count=Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies").Count;
                Assert.LessOrEqual(count-previous,1);if(i<6)Assert.AreEqual(0,count);previous=count;
            }
            Assert.That(previous,Is.InRange(2,6));Assert.AreEqual(0,Get<SurvivorHordeState>("horde").TotalCount);
        }
        [Test] public void VisibilityFarBirthKeepsLogicWithoutCreatingPresentation()
        {
            PrepareHordeCheck();var state=Get<SurvivorHordeState>("horde");var kind=Get<SurvivorEnemyRoster>("enemyRoster").Entries[0].EnemyId;
            state.Add(0,kind,25,20,2,1);Call("TickHorde",.01f);
            Assert.AreEqual(1,Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies").Count);
            Assert.AreEqual(0,view.Wakes,"视野外出生只建逻辑，不借用角色实例");
        }
        [Test] public void VisibilitySwitchPreservesIdentityHealthAndPursuit()
        {
            PrepareHordeCheck();var active=Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies");
            var e=new ArcadeEnemyRuntime{Id=902,ConfigId=config.EnemyId,Position=Vector2.zero,Health=23,MaxHealth=100,Speed=2};active.Add(e.Id,e);
            Call("SyncEnemyPresentations");Assert.AreEqual(1,view.Wakes);
            e.Position=new Vector2(25,20);Call("SyncEnemyPresentations");Call("SyncEnemyPresentations");Assert.AreEqual(1,view.Suspends);
            float before=e.Position.magnitude;Call("TickEnemies",.1f);Assert.Less(e.Position.magnitude,before);
            view.Bounds=new Rect(20,15,10,10);Call("SyncEnemyPresentations");Assert.AreEqual(2,view.Wakes);
            Assert.AreSame(e,active[902]);Assert.AreEqual(23,e.Health);Assert.AreEqual(23,view.LastWakeHealth);
            Assert.AreEqual(0,model.Kills);Assert.AreEqual(0,Get<Dictionary<int,(Vector2 Position,long Experience)>>("drops").Count);
        }
        [Test] public void VisibilityUsesDifferentEnterAndExitBounds()
        {
            PrepareHordeCheck();var e=new ArcadeEnemyRuntime{Id=903,ConfigId=config.EnemyId,Position=Vector2.zero,Health=10,MaxHealth=10};
            Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies").Add(e.Id,e);Call("SyncEnemyPresentations");
            e.Position=new Vector2(9,0);Call("SyncEnemyPresentations");Assert.AreEqual(0,view.Suspends);
            e.Position=new Vector2(20,0);Call("SyncEnemyPresentations");Assert.AreEqual(1,view.Suspends);
            e.Position=new Vector2(9,0);Call("SyncEnemyPresentations");Assert.AreEqual(1,view.Wakes);
            e.Position=new Vector2(6,0);Call("SyncEnemyPresentations");Assert.AreEqual(2,view.Wakes);
        }
        [Test] public void VisibilityWakeFailureKeepsAuthorityAndStopsPlay()
        {
            PrepareHordeCheck();var e=new ArcadeEnemyRuntime{Id=904,ConfigId=config.EnemyId,Position=Vector2.zero,Health=23,MaxHealth=100};
            var active=Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies");active.Add(e.Id,e);view.FailWakeAfter=0;
            Call("SyncEnemyPresentations");Assert.IsTrue(model.Paused);Assert.IsNotEmpty(system.LastError);Assert.AreSame(e,active[904]);Assert.AreEqual(23,e.Health);
        }
        [Test] public void VisibilityHiddenEnemyStillTakesProjectileDamageAndDropsExperience()
        {
            // 镜头外但仍在当前攻击亮区内，不能用超出昼夜射程的点检验隐藏表现。
            PrepareHordeCheck();KillOne(new Vector2(12,8));
            Assert.AreEqual(1,model.Kills);Assert.AreEqual(0,view.Wakes);
            Assert.IsEmpty(Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies"));
            Assert.AreEqual(new Vector2(12,8),Get<Dictionary<int,(Vector2 Position,long Experience)>>("drops").Values.Single().Position);
        }
        private sealed class TestView:IArcadeCombatViewPort
        {
            public int ElementStateWrites;
            public bool LastElementFire,LastElementFrost;
            public void SetEnemyElementState(int id,bool fire,bool frost){ElementStateWrites++;LastElementFire=fire;LastElementFrost=frost;}
            public bool HordeEnabled;
            public Rect Bounds;
            public int Wakes,Suspends,LastWakeHealth,FailWakeAfter=int.MaxValue;
            public bool TryGetSurvivorViewBounds(out Rect bounds){bounds=Bounds;return HordeEnabled;}
            public bool TryWakeSurvivorEnemy(int id,string configId,Vector2 position,int health,int maximum,int sequence,out string error)
            {error=Wakes>=FailWakeAfter?"模拟展开失败":null;if(error!=null)return false;Wakes++;LastWakeHealth=health;return true;}
            public void SuspendSurvivorEnemy(int id){Suspends++;}
            public int WarmCalls;
            public bool WarmFailure;
            public bool TryPrewarmSurvivorEnemy(string id,int count,out bool complete,out string error){WarmCalls++;complete=!WarmFailure&&WarmCalls>=2;error=WarmFailure?"模拟预热失败":null;return !WarmFailure;}
            public bool ReloadVisible;
            public bool AmmoVisible;
            public int AmmoCurrent,AmmoCapacity;
            public void SetPlayerAmmo(bool visible,int current,int capacity){AmmoVisible=visible;AmmoCurrent=current;AmmoCapacity=capacity;}
            public float ReloadProgress;
            public void SetPlayerReload(bool active,float progress){ReloadVisible=active;ReloadProgress=progress;}
            public Func<Vector2,bool> SpawnHidden=p=>true;
            public bool IsArenaSpawnHidden(Vector2 position,float radius)=>SpawnHidden(position);
            public bool TargetVisible=true;
            public bool IsArenaTargetVisible(Vector2 position)=>TargetVisible;
            public bool Ready=true;
            public bool IsRoundLayoutReady=>Ready;
            public int PlayerWrites,CombatPoseWrites,Ends,RemovedDrops,GoldFeedbacks;
            public Vector2 LastGoldPosition;
            public void PlaySurvivorGoldPickup(long amount,Vector2 position){GoldFeedbacks++;LastGoldPosition=position;}
            public bool IsActive=>true;
            public void Begin(int n,float w,float h,IReadOnlyList<ArcadeObstacleDefinition> o){}
            public void End(){Ends++;}
            public bool TryScreenToArena(Vector2 s,out Vector2 p){p=default;return false;}
            public void SetPlayer(Vector2 p,Vector2 f,bool v,bool i){PlayerWrites++;}
            public void SetPlayerCombatState(Vector2 facing,bool invulnerable){CombatPoseWrites++;}
            public void SetPlayerHealth(int c,int m){}
            public void SetEnemy(int id,string c,Vector2 p,int hp,int max,bool t=false,int seq=0,bool bar=false){}
            public void RemoveEnemy(int id,bool death=false){}
            public void SetProjectile(int id,bool h,Vector2 p,Vector2 d){}
            public void RemoveProjectile(int id){}
            public void SetDrop(int id,ArcadeDropDefinition d,Vector2 p){}
            public void RemoveDrop(int id){RemovedDrops++;}
            public void SetRemotePlayer(long id,Vector2 p,Vector2 f,bool v,bool i,int c,int m){}
            public void RemoveRemotePlayer(long id){}
            public void TickPlayerFeedback(float d){}
        }
    }
}
