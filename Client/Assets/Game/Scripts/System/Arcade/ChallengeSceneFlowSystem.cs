using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using QFramework;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game
{
    /// <summary>独立挑战入场、暂停和退出事务；连续意图不经过幸存者或闯关战斗。</summary>
    public sealed class ChallengeSceneFlowSystem : AbstractSystem
    {
        private ChallengeWorldCtrl world;
        private CancellationTokenSource entry;
        private IUnRegister stateRegistration;
        private bool busy, released;
        private long nextInstance;
        private float moveSpeed;
        public bool HasWorld => world?.Ready==true && this.GetModel<ChallengeModel>().Snapshot.Active;
        public Vector3 PlayerPosition => world?.Position??Vector3.zero;

        protected override void OnInit()
        {
            LifeCycle.RegisterUpdate(Tick);
            stateRegistration=this.RegisterEvent<AppStateChangedEvent>(_=>
                this.GetSystem<ChallengeSystem>().SetPaused(this.GetModel<AppStateModel>().CurrentState.Value!=AppState.Playing));
        }
        protected override void OnDeinit()
        {
            released=true;entry?.Cancel();stateRegistration?.UnRegister();LifeCycle.UnregisterUpdate(Tick);
            world?.Dispose();world=null;
        }

        /// <summary>保持原菜单直到地图、人物与配置准备完毕；失败和菜单失效均回收候选。</summary>
        public async UniTask<string> EnterAsync(Func<bool> isCurrent)
        {
            if(released||busy||HasWorld)return "挑战正在切换";
            if(!string.IsNullOrEmpty(this.GetSystem<MapSceneSystem>().CurrentMapId))return "请先返回主菜单";
            busy=true;entry=new CancellationTokenSource();
            var timeout=entry.CancelAfterSlim(TimeSpan.FromSeconds(30),DelayType.Realtime);
            var candidate=new ChallengeWorldCtrl(ResourceLoader.Current,++nextInstance);
            bool began=false;
            try
            {
                if(!this.GetUtility<IRuntimeConfigProvider>().TryGetPlayer("player.graybox.default",out var player,out var error))return error;
                await candidate.PrepareAsync(player.PrefabAddress,entry.Token);
                if(released||!isCurrent())throw new OperationCanceledException();
                if(!this.GetSystem<ChallengeSystem>().TryBegin(out error))return error;
                began=true;
                // 先准备已采用HUD，失败仍可完整回到菜单。
                UIHelper.ShowUI<ChallengeHudUICtrl>();
                candidate.Activate();world=candidate;candidate=null;moveSpeed=player.MoveSpeed;
                this.GetSystem<AppStateSystem>().TryTransition(AppState.Loading);
                this.GetSystem<AppStateSystem>().TryTransition(AppState.Playing);
                UIHelper.HideUI<StartUICtrl>();
                return null;
            }
            catch(Exception exception)
            {
                world?.Dispose();world=null;
                if(began){this.GetSystem<ChallengeSystem>().Stop();UIHelper.HideUI<ChallengeHudUICtrl>();}
                return exception is OperationCanceledException?"挑战入场已取消或超时":exception.Message;
            }
            finally{candidate?.Dispose();timeout.Dispose();entry.Dispose();entry=null;busy=false;}
        }

        /// <summary>释放本次现场及弹药，不写其他模式存档；再次退出不重复释放。</summary>
        public void ReturnToMenu()
        {
            if(!HasWorld)return;
            this.GetSystem<ChallengeSystem>().Stop();world.Dispose();world=null;
            this.GetSystem<AppStateSystem>().TryPause();
            UIHelper.HideUI<ChallengeHudUICtrl>();UIHelper.HideUI<PauseUICtrl>();UIHelper.ShowUI<StartUICtrl>();
        }

        /// <summary>菜单卸载或离开时取消候选，不等待资源超时才归还句柄。</summary>
        public void CancelEntry() => entry?.Cancel();

        /// <summary>WASD移动、鼠标手动扣扳机、R换弹、1/2切枪；靠近车按E补当前枪备弹。</summary>
        private void Tick()
        {
            if(!HasWorld||this.GetModel<AppStateModel>().CurrentState.Value!=AppState.Playing||UIHelper.HasUIOpen)return;
            var keys=Keyboard.current;var mouse=Mouse.current;var rules=this.GetSystem<ChallengeSystem>();
            var hotkeys=this.GetSystem<HotKeySystem>();
            if(hotkeys.IsCapturingRebind||hotkeys.IsTextInputFocused())return;
            Vector2 move=Vector2.zero;
            if(keys!=null)
            {
                move=new Vector2((keys.dKey.isPressed?1:0)-(keys.aKey.isPressed?1:0),(keys.wKey.isPressed?1:0)-(keys.sKey.isPressed?1:0));
                if(keys.rKey.wasPressedThisFrame)rules.TryReload();
                if(keys.digit1Key.wasPressedThisFrame)rules.TrySwitch(0);
                if(keys.digit2Key.wasPressedThisFrame)rules.TrySwitch(1);
                if(keys.eKey.wasPressedThisFrame)TrySupply();
            }
            world.Move(move,moveSpeed,Mathf.Min(Time.deltaTime,.1f));
            if(mouse!=null)world.Aim(mouse.position.ReadValue());
            bool fired=mouse!=null&&mouse.leftButton.isPressed&&rules.TryFire();
            world.Present(this.GetModel<ChallengeModel>().Snapshot.Slot,Time.deltaTime,fired);
        }

        /// <summary>只允许同层且距补给车交互点三米内补充备弹；弹匣仍需换弹。</summary>
        public bool TrySupply()
        {
            if(!HasWorld||this.GetModel<AppStateModel>().CurrentState.Value!=AppState.Playing)return false;
            if(Vector3.Distance(world.Position,world.SupplyPosition)>3)return false;
            return this.GetSystem<ChallengeSystem>().Supply(int.MaxValue)>0;
        }
    }
}
