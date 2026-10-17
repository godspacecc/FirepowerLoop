using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Game.Tests
{
    /// <summary>单人正式输入在双摇杆和鼠标同时存在时，按当前有效瞄准来源裁决方向。</summary>
    public sealed class ArcadeAimPriorityTests
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private SlayerMartAbilityFlowTests h;
        private PlayerInputCtrl input;
        private Gamepad pad;
        private Mouse mouse;
        private Vector2 sampledStick;
        private bool previousEditorInput;
        private const string EditorInputFlag="RUN_PLAYER_UPDATES_IN_EDIT_MODE";
        [SetUp] public void Setup()
        {
            previousEditorInput=(bool)typeof(InputSettings).GetMethod("IsFeatureEnabled",Private).Invoke(InputSystem.settings,new object[]{EditorInputFlag});
            InputSystem.settings.SetInternalFeatureFlag(EditorInputFlag,true);
            h=new SlayerMartAbilityFlowTests();h.Setup();input=new PlayerInputCtrl();
            pad=InputSystem.AddDevice<Gamepad>();mouse=InputSystem.AddDevice<Mouse>();input.Enable();
            typeof(ArcadeCombatSystem).GetField("input",Private).SetValue(h.system,input);
            typeof(ArcadeCombatSystem).GetField("view",Private).SetValue(h.system,new View());
            InputSystem.QueueStateEvent(mouse,new MouseState{position=new Vector2(123,234)});InputSystem.Update();
        }
        [TearDown] public void Cleanup(){input.Dispose();InputSystem.RemoveDevice(pad);if(mouse!=null)InputSystem.RemoveDevice(mouse);InputSystem.settings.SetInternalFeatureFlag(EditorInputFlag,previousEditorInput);}
        private void Read(Vector2 stick,bool held=false)
        {
            // 夹具临时允许编辑模式处理Dynamic输入；结束时恢复，不写入项目设置。
            InputSystem.QueueStateEvent(pad,new GamepadState{rightStick=stick});
            InputSystem.QueueStateEvent(mouse,new MouseState{position=new Vector2(123,234),buttons=(ushort)(held?1:0)});
            InputSystem.Update();
            sampledStick=input.AimInput;
            typeof(ArcadeCombatSystem).GetMethod("ReadInput",Private).Invoke(h.system,new object[]{h.runtime});
        }
        [Test] public void ActiveStickWinsOverIdleMouse(){Read(Vector2.right);Assert.That(sampledStick.x,Is.GreaterThan(.9));Assert.That(h.runtime.AimDirection,Is.EqualTo(Vector2.right));Assert.That(h.runtime.Firing,Is.True);}
        [Test] public void ActiveStickWinsOverPressedMouse(){Read(Vector2.up,true);Assert.That(h.runtime.AimDirection,Is.EqualTo(Vector2.up));Assert.That(h.runtime.Firing,Is.True);}
        [Test] public void ReleasedStickRestoresMouseAim(){Read(Vector2.right);Read(Vector2.zero);Assert.That(h.runtime.AimDirection,Is.EqualTo(Vector2.left));Assert.That(h.runtime.Firing,Is.False);}
        [Test] public void ReleasedStickAllowsMouseFire(){Read(Vector2.zero,true);Assert.That(h.runtime.AimDirection,Is.EqualTo(Vector2.left));Assert.That(h.runtime.Firing,Is.True);}
        [Test] public void StickBelowDeadZoneUsesMouse(){Read(new Vector2(.05f,0));Assert.That(h.runtime.AimDirection,Is.EqualTo(Vector2.left));Assert.That(h.runtime.Firing,Is.False);}
        private sealed class View : IArcadeCombatViewPort
        {
            public bool IsActive=>true;
            public void Begin(int n,float w,float h,IReadOnlyList<ArcadeObstacleDefinition> o){}
            public void End(){}
            public bool TryScreenToArena(Vector2 s,out Vector2 p){p=Vector2.left*10;return true;}
            public void SetPlayer(Vector2 p,Vector2 f,bool v,bool i){}
            public void SetPlayerHealth(int c,int m){}
            public void SetEnemy(int id,string config,Vector2 p,int c,int m,bool telegraph=false,int attackSequence=0,bool persistentHealthBar=false){}
            public void RemoveEnemy(int id,bool playDeath=false){}
            public void SetProjectile(int id,bool hostile,Vector2 p,Vector2 d){}
            public void RemoveProjectile(int id){}
            public void SetDrop(int id,ArcadeDropDefinition d,Vector2 p){}
            public void RemoveDrop(int id){}
            public void SetRemotePlayer(long id,Vector2 p,Vector2 f,bool v,bool i,int c,int m){}
            public void RemoveRemotePlayer(long id){}
            public void TickPlayerFeedback(float s){}
        }
    }
}
