using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Game.Tests
{
    /// <summary>真实Input System虚拟设备验证双肩组合；不把虚拟设备结果称为真实手柄验收。</summary>
    public sealed class SlayerMartFieldInputTests
    {
        private Gamepad pad;private int frame;
        [SetUp]public void Setup(){frame=1;pad=InputSystem.AddDevice<Gamepad>();}
        [TearDown]public void Cleanup(){if(pad!=null)InputSystem.RemoveDevice(pad);}
        private static object Call(object target,string method,params object[] args)
        {var m=target.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);Assert.That(m,Is.Not.Null,"缺少输入入口："+method);return m.Invoke(target,args);}
        private static IDisposable Chord()
        {var type=typeof(OnlineArcadeInputController).Assembly.GetType("Game.ArcadeFieldUpgradeChord");Assert.That(type,Is.Not.Null,"缺少双肩技能采集器");var value=(IDisposable)Activator.CreateInstance(type);Call(value,"Enable");return value;}
        private void Press(params GamepadButton[] buttons)
        {var state=new GamepadState();foreach(var button in buttons)state=state.WithButton(button);InputSystem.QueueStateEvent(pad,state);InputSystem.Update();}
        [Test]public void SingleShoulderDoesNotEmitUpgrade()
        {using var c=Chord();Press(GamepadButton.LeftShoulder);Assert.That(Call(c,"TryConsume"),Is.False);}
        [Test]public void DifferentGamepadsCannotFormOneChord()
        {using var c=Chord();var other=InputSystem.AddDevice<Gamepad>();try{Press(GamepadButton.LeftShoulder);InputSystem.QueueStateEvent(other,new GamepadState().WithButton(GamepadButton.RightShoulder));InputSystem.Update();Assert.That(Call(c,"TryConsume"),Is.False);}finally{InputSystem.RemoveDevice(other);}}
        [Test]public void LeftThenRightEmitsExactlyOnceWhileHeld()
        {using var c=Chord();Press(GamepadButton.LeftShoulder);Press(GamepadButton.LeftShoulder,GamepadButton.RightShoulder);Assert.That(Call(c,"TryConsume"),Is.True);Assert.That(Call(c,"TryConsume"),Is.False);InputSystem.Update();Assert.That(Call(c,"TryConsume"),Is.False);}
        [Test]public void RightThenLeftAlsoEmitsExactlyOnce()
        {using var c=Chord();Press(GamepadButton.RightShoulder);Press(GamepadButton.LeftShoulder,GamepadButton.RightShoulder);Assert.That(Call(c,"TryConsume"),Is.True);Assert.That(Call(c,"TryConsume"),Is.False);}
        [Test]public void ReleasingAndPressingAgainCreatesNewIntent()
        {using var c=Chord();Press(GamepadButton.LeftShoulder,GamepadButton.RightShoulder);Call(c,"TryConsume");Press();Press(GamepadButton.LeftShoulder,GamepadButton.RightShoulder);Assert.That(Call(c,"TryConsume"),Is.True);}
        [Test]public void DisableClearsPendingChordAndDoesNotReplayHeldButtons()
        {using var c=Chord();Press(GamepadButton.LeftShoulder,GamepadButton.RightShoulder);Call(c,"Disable");Call(c,"Enable");InputSystem.Update();Assert.That(Call(c,"TryConsume"),Is.False);}
        private OnlineArcadeInputController Controller()
        {var ctor=typeof(OnlineArcadeInputController).GetConstructor(new[]{typeof(Func<int>)});Assert.That(ctor,Is.Not.Null,"缺少按帧隔离的输入脉冲");var value=(OnlineArcadeInputController)ctor.Invoke(new object[]{new Func<int>(()=>frame)});value.Enable();return value;}
        private static bool Upgrade(OnlineArcadeInputFrame value)
        {var p=typeof(OnlineArcadeInputFrame).GetProperty("FieldUpgrade");Assert.That(p,Is.Not.Null,"输入帧缺少独立技能标志");return (bool)p.GetValue(value);}
        private static void Ack(OnlineArcadeInputController c,bool field)
        {var method=typeof(OnlineArcadeInputController).GetMethod("Acknowledge");Assert.That(method.GetParameters().Length,Is.EqualTo(3));method.Invoke(c,new object[]{false,false,field});}
        private static int Count(OnlineArcadeInputController c)
        {var p=typeof(OnlineArcadeInputController).GetProperty("QueuedFieldUpgrades",BindingFlags.Instance|BindingFlags.NonPublic);Assert.That(p,Is.Not.Null);return (int)p.GetValue(c);}
        [Test]public void ChordSuppressesSameFrameBombAndDash()
        {using var c=Controller();Press(GamepadButton.LeftShoulder,GamepadButton.RightShoulder,GamepadButton.West,GamepadButton.South);var value=c.Read(null,Vector2.zero);Assert.That(Upgrade(value),Is.True);Assert.That(value.Bomb||value.Dash,Is.False);Assert.That(c.QueuedBombs+c.QueuedDashes,Is.Zero);}
        [Test]public void UpgradeQueuedBeforeConsumablesAlsoHasPriority()
        {using var c=Controller();Call(c,"QueueFieldUpgrade");c.QueueBomb();c.QueueDash();var value=c.Read(null,Vector2.zero);Assert.That(Upgrade(value),Is.True);Assert.That(value.Bomb||value.Dash,Is.False);Assert.That(c.QueuedBombs+c.QueuedDashes,Is.Zero);}
        [Test]public void TwoUpgradeSourcesInSameFrameProduceOnePulse()
        {using var c=Controller();Call(c,"QueueFieldUpgrade");Call(c,"QueueFieldUpgrade");Assert.That(Count(c),Is.EqualTo(1));}
        [Test]public void AcknowledgementConsumesOnlyOneCapturedUpgrade()
        {using var c=Controller();Call(c,"QueueFieldUpgrade");frame++;Call(c,"QueueFieldUpgrade");Ack(c,true);Assert.That(Count(c),Is.EqualTo(1));}
        [Test]public void PreviousFrameBombIsDeferredRatherThanLost()
        {using var c=Controller();c.QueueBomb();frame++;Call(c,"QueueFieldUpgrade");Assert.That(c.Read(null,Vector2.zero).Bomb,Is.False);Ack(c,true);Assert.That(c.Read(null,Vector2.zero).Bomb,Is.True);}
        [Test]public void DisableClearsAllQueuedPulses()
        {using var c=Controller();c.QueueBomb();c.QueueDash();Call(c,"QueueFieldUpgrade");c.Disable(true);Assert.That(Count(c)+c.QueuedBombs+c.QueuedDashes,Is.Zero);}
    }
}
