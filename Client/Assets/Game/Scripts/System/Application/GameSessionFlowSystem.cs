using System.Threading;
using Cysharp.Threading.Tasks;
using QFramework;

namespace Game
{
    /// <summary>仅负责按需创建基地运行世界；副本进入与返回由 ExpeditionFlowSystem 编排。</summary>
    public sealed class GameSessionFlowSystem : AbstractSystem
    {
        protected override void OnInit()
        {
        }

        public async UniTask<GameSessionFlowResult> EnterBaseAsync(
            BaseSaveData baseData,
            CancellationToken cancellationToken = default)
        {
            if (baseData == null)
            {
                return GameSessionFlowResult.Fail("缺少基地存档", true);
            }

            var saves = this.GetSystem<SaveSystem>();
            var before = saves.CaptureRuntimeSnapshot();
            if (!saves.CommitBaseRuntime(baseData, out var commitError))
            {
                return GameSessionFlowResult.Fail(commitError, true);
            }

            var mapResult = await this.GetSystem<MapSceneSystem>().LoadBaseMapAsync(baseData, cancellationToken);
            if (mapResult.Success)
            {
                // 启动恢复基地时重显未读结果；展示入口不参与奖励事务。
                CivilisationSettlementUICtrl.TryShowPending();
                return GameSessionFlowResult.Ok();
            }

            saves.RestoreRuntimeSnapshot(before, out var rollbackError);
            this.GetSystem<AppStateSystem>().TryRestoreStableStateAfterLoading(AppState.Paused);
            return GameSessionFlowResult.Fail(
                string.IsNullOrWhiteSpace(rollbackError)
                    ? $"进入基地时加载地图失败：{mapResult.Error}"
                    : $"进入基地时加载地图失败：{mapResult.Error}；运行态回滚失败：{rollbackError}",
                string.IsNullOrWhiteSpace(rollbackError));
        }
    }
}
