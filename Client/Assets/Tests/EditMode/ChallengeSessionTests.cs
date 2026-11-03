using System;
using System.IO;
using System.Reflection;
using Luban;
using NUnit.Framework;
using QFramework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>挑战独立会话的暂停、切枪、重开和失败事务验证。</summary>
    public sealed class ChallengeSessionTests
    {
        private Game architecture;
        private object system, model;
        private Tables tables;
        private static object Call(object target,string name,params object[] args) => target.GetType().GetMethod(name).Invoke(target,args);
        private object Snapshot => model.GetType().GetProperty("Snapshot").GetValue(model);
        private T Read<T>(string name) => (T)Snapshot.GetType().GetField(name).GetValue(Snapshot);

        [SetUp] public void Setup()
        {
            var assembly=typeof(SurvivorModel).Assembly;
            var modelType=assembly.GetType("Game.ChallengeModel");
            var systemType=assembly.GetType("Game.ChallengeSystem");
            Assert.That(modelType,Is.Not.Null,"独立挑战模型尚未实现");
            Assert.That(systemType,Is.Not.Null,"独立挑战系统尚未实现");
            architecture=new Game(); model=Activator.CreateInstance(modelType);system=Activator.CreateInstance(systemType);
            architecture.GetType().GetMethod("RegisterModel").MakeGenericMethod(modelType).Invoke(architecture,new[]{model});
            tables=new Tables(name=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",name+".bytes"))));
            architecture.RegisterUtility<IRuntimeConfigProvider>(new LubanRuntimeConfigProvider(()=>tables));
            ((ICanSetArchitecture)system).SetArchitecture(architecture);
        }
        [TearDown] public void Cleanup() { if(system!=null) Call(system,"Stop"); }
        private void Begin() { var args=new object[]{null};Assert.That(Call(system,"TryBegin",args),Is.True,args[0] as string); }

        [Test] public void PauseRejectsInputAndFreezesReload()
        {
            Begin();Call(system,"TryFire");Call(system,"TryReload");
            float remaining=Read<float>("ReloadRemaining");
            Call(system,"SetPaused",true);Call(system,"Advance",100f);
            Assert.That(Call(system,"TryFire"),Is.False);
            Assert.That(Call(system,"TrySwitch",1),Is.False);
            Assert.That(Read<float>("ReloadRemaining"),Is.EqualTo(remaining));
            Call(system,"SetPaused",false);Call(system,"Advance",remaining);
            Assert.That(Read<int>("Magazine"),Is.EqualTo(Read<int>("MagazineCapacity")));
        }
        [Test] public void SwitchingCancelsReloadAndKeepsEachMagazine()
        {
            Begin();int original=Read<int>("Magazine");int reserve=Read<int>("Reserve");
            Call(system,"TryFire");Call(system,"TryReload");
            Assert.That(Call(system,"TrySwitch",0),Is.False);
            Assert.That(Read<float>("ReloadRemaining"),Is.GreaterThan(0));
            Assert.That(Call(system,"TrySwitch",1),Is.True);Call(system,"TryFire");Call(system,"Advance",10f);
            Assert.That(Call(system,"TrySwitch",0),Is.True);
            Assert.That(Read<int>("Magazine"),Is.EqualTo(original-1));
            Assert.That(Read<int>("Reserve"),Is.EqualTo(reserve));
            Assert.That(Read<float>("ReloadRemaining"),Is.Zero);
        }
        [Test] public void StopAndRestartResetAmmoWithoutOldModeModels()
        {
            Begin();int original=Read<int>("Magazine");Call(system,"TryFire");Call(system,"Stop");
            Assert.That(Read<bool>("Active"),Is.False);Assert.That(Call(system,"TryFire"),Is.False);
            Begin();Assert.That(Read<int>("Magazine"),Is.EqualTo(original));
            // 架构未注册任何旧模式模型，独立开局仍须成功。
            Assert.That(architecture.GetModel<SurvivorModel>(),Is.Null);
        }
        [Test] public void MissingConfigLeavesSessionInactive()
        {
            architecture.RegisterUtility<IRuntimeConfigProvider>(new LubanRuntimeConfigProvider(()=>throw new InvalidDataException("missing")));
            var args=new object[]{null};Assert.That(Call(system,"TryBegin",args),Is.False);
            Assert.That(Read<bool>("Active"),Is.False);Assert.That(args[0],Is.Not.Null);
        }
    }
}
