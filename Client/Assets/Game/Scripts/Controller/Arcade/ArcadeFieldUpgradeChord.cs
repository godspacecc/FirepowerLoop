using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;
using UnityEngine;

namespace Game
{
    /// <summary>同一手柄双肩同时按住产生一次意图；松开后才可再次触发。</summary>
    public sealed class ArcadeFieldUpgradeChord : IDisposable
    {
        private readonly HashSet<int> held = new HashSet<int>();
        private bool enabled, pending;
        private int pressedFrame=int.MinValue;
        public bool WasPressedThisFrame=>enabled&&pressedFrame==Time.frameCount;
        public void Enable()
        {
            if(enabled)return;
            enabled=true;pending=false;held.Clear();
            foreach(var pad in Gamepad.all)if(Both(pad))held.Add(pad.deviceId);
            InputSystem.onAfterUpdate+=Sample;
        }
        private static bool Both(Gamepad pad)=>pad.leftShoulder.isPressed&&pad.rightShoulder.isPressed;
        private void Sample()
        {
            foreach(var pad in Gamepad.all)
            {
                if(!Both(pad)){held.Remove(pad.deviceId);continue;}
                if(held.Add(pad.deviceId)){pending=true;pressedFrame=Time.frameCount;}
            }
        }
        public bool TryConsume(){bool value=pending;pending=false;return value;}
        /// <summary>清除菜单期间脉冲，保留按住状态以免回到战斗补放。</summary>
        public void ClearPulse(){pending=false;pressedFrame=int.MinValue;}
        public void Disable(){if(enabled)InputSystem.onAfterUpdate-=Sample;enabled=false;pending=false;pressedFrame=int.MinValue;held.Clear();}
        public void Dispose()=>Disable();
    }
}
