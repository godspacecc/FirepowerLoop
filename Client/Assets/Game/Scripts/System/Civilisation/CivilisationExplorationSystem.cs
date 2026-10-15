using System;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>裁决线索发现及捷径开放并保存稳定身份；不生成地形、不直接写 UI 或物品奖励。</summary>
    public sealed class CivilisationExplorationSystem:AbstractSystem
    {
        private bool busy;
        protected override void OnInit(){ }
        private bool TryContext(long instance,out GeneratedValleyContentHandle handle,out Vector3 position,out string error)
        {
            handle=this.GetModel<MapSceneModel>().CurrentHandle as GeneratedValleyContentHandle;position=default;error="civilisation.error.context";
            var map=this.GetModel<MapSceneModel>();
            if(UIHelper.HasUIOpen||this.GetModel<AppStateModel>().CurrentState.Value!=AppState.Playing||map.Status.Value!=MapLoadStatus.Loaded||map.CurrentMode!=MapPreparationMode.OfflineFull||
                handle==null||!handle.IsValid||this.GetModel<SaveModel>().CurrentDomain!=OfflineSaveDomain.Expedition||this.GetModel<TemporaryBuildingModel>().Mode!=TemporaryBuildingMode.Normal||
                !this.GetModel<UnitModel>().TryGet(PlayerModel.LocalPlayerUnitId,out var player)||!player.IsActive||player.MapInstanceId!=instance||this.GetModel<CombatModel>().PlayerHP.Value<=0)return false;
            position=player.Position;error=null;return true;
        }
        /// <summary>靠近线索首次读取时保存；已发现线索允许重读，不重复提交或增加奖励。</summary>
        public bool TryReadClue(long mapInstanceId,string clueId,out string messageKey,out string error)
        {
            messageKey=null;
            if(!TryContext(mapInstanceId,out var handle,out var position,out error))return false;
            var clue=handle.Layout.Clues.FirstOrDefault(c=>c.Id==clueId);
            if(clue==null||(position-clue.Pose.ToVector()).sqrMagnitude>2.25f*2.25f){error="civilisation.error.distance";return false;}
            var model=this.GetModel<CivilisationExpeditionModel>();messageKey=clue.MessageKey;
            if(model.HasReadClue(clueId) && model.Records.Contains(clue.MessageKey + ".record"))return true;
            var save=this.GetSystem<SaveSystem>();var before=save.CaptureRuntimeSnapshot();
            try{model.ReadClue(clueId);model.RecordReading(clue.MessageKey + ".record");save.MarkDirty("valley clue");if(save.Flush(out error))return true;}
            catch(Exception e){error=e.Message;}
            save.RestoreRuntimeSnapshot(before,out _);return false;
        }
        /// <summary>先开放指定地图的导航，再保存发现状态；保存失败关闭旧地图捷径并恢复领域快照。</summary>
        public async UniTask<MapControllerResult> DiscoverReturnShortcutAsync(long mapInstanceId,string shortcutId)
        {
            if(busy)return MapControllerResult.Fail("civilisation.error.context");
            if(!TryContext(mapInstanceId,out var handle,out var position,out var error)||handle.Layout.ShortcutId!=shortcutId)return MapControllerResult.Fail(error??"civilisation.error.context");
            if(Vector3.Distance(position,handle.Layout.Camp.ToVector())>8)return MapControllerResult.Fail("civilisation.error.distance");
            var model=this.GetModel<CivilisationExpeditionModel>();if(model.HasShortcut(shortcutId))return MapControllerResult.Ok();
            var save=this.GetSystem<SaveSystem>();var before=save.CaptureRuntimeSnapshot();var builder=this.GetUtility<IValleyMapBuilder>();
            if(builder==null)return MapControllerResult.Fail("civilisation.error.context");
            busy=true;bool opened=false;
            try
            {
                var result=await builder.SetShortcutOpenAsync(handle,shortcutId,true,CancellationToken.None);
                if(!result.Success)return result;opened=true;
                if(!TryContext(mapInstanceId,out var current,out _,out error)||!ReferenceEquals(handle,current))throw new InvalidOperationException("civilisation.error.context");
                model.DiscoverShortcut(shortcutId);save.MarkDirty("valley shortcut");if(save.Flush(out error))return MapControllerResult.Ok();
                throw new InvalidOperationException(error??"civilisation.error.save");
            }
            catch(Exception e)
            {
                // 地图切换后的迟到结果不能把旧快照覆盖到新领域；只清理仍有效的旧句柄。
                var same=ReferenceEquals(this.GetModel<MapSceneModel>().CurrentHandle,handle);
                if(same)save.RestoreRuntimeSnapshot(before,out _);
                if(opened&&handle.IsValid)
                {
                    MapControllerResult rollback;
                    try{rollback=await builder.SetShortcutOpenAsync(handle,shortcutId,false,CancellationToken.None);}
                    catch(Exception rollbackError){rollback=MapControllerResult.Fail(rollbackError.Message);}
                    // 回滚也存在异步边界，暂停之前重验地图，避免暂停已经提交的新地图。
                    if(!rollback.Success&&ReferenceEquals(this.GetModel<MapSceneModel>().CurrentHandle,handle))
                    {this.GetSystem<AppStateSystem>().TryRestoreStableStateAfterLoading(AppState.Paused);return MapControllerResult.Fail(e.Message+"；捷径回滚失败："+rollback.Error);}
                }
                return MapControllerResult.Fail(e.Message);
            }
            finally{busy=false;}
        }
        public static string DirectionKey(Vector3 direction)
        {
            var angle=Mathf.Atan2(direction.x,direction.z)*Mathf.Rad2Deg;
            var index=(Mathf.RoundToInt(angle/45)+8)%8;
            return new[]{"civilisation.direction.n","civilisation.direction.ne","civilisation.direction.e","civilisation.direction.se","civilisation.direction.s","civilisation.direction.sw","civilisation.direction.w","civilisation.direction.nw"}[index];
        }
    }
}
