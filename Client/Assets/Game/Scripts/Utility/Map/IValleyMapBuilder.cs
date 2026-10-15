using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using QFramework;

namespace Game
{
    public interface IValleyMapBuilder:IUtility
    {
        UniTask<MapContentLoadResult> BuildAsync(IMapContentHandle baseHandle,ValleyLayoutSaveData layout,ValleyGenerationProfile profile,CancellationToken token);
        UniTask<MapControllerResult> SetShortcutOpenAsync(GeneratedValleyContentHandle handle,string shortcutId,bool isOpen,CancellationToken token);
    }

    /// <summary>候选构建进度只覆盖生成准备；正式显示和总进度由地图事务负责。</summary>
    public interface IValleyMapProgressBuilder
    {
        UniTask<MapContentLoadResult> BuildWithProgressAsync(IMapContentHandle baseHandle,ValleyLayoutSaveData layout,ValleyGenerationProfile profile,CancellationToken token,Action<float> onProgress);
        UniTask<MapContentLoadResult> BuildWithStagesAsync(IMapContentHandle baseHandle,ValleyLayoutSaveData layout,ValleyGenerationProfile profile,CancellationToken token,Action<float> onProgress,Action<MapLoadingStage> onStage);
    }
}
