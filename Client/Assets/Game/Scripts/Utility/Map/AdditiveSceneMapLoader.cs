using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using YooAsset;

namespace Game
{
    /// <summary>
    /// 通过 YooAsset 附加加载地图场景，并在返回成功句柄前完成上下文验证和 MapContentRoot 隐藏。
    /// </summary>
    public sealed class AdditiveSceneMapLoader : IMapContentLoader
    {
        public UniTask<MapContentLoadResult> LoadAsync(string mapId, string address, Action<float> onProgress, CancellationToken cancellationToken = default)
        {
            return LoadInternal(mapId, address, onProgress, cancellationToken);
        }

        public UniTask<MapContentUnloadResult> UnloadAsync(IMapContentHandle handle, CancellationToken cancellationToken = default)
        {
            return UnloadInternal(handle, cancellationToken);
        }

        private static async UniTask<MapContentLoadResult> LoadInternal(string mapId, string address, Action<float> onProgress, CancellationToken cancellationToken)
        {
            var package = YooAssets.TryGetPackage("DefaultPackage");
            if (package == null)
            {
                return MapContentLoadResult.Fail("地图加载失败：DefaultPackage 为空");
            }

            var sceneHandle = package.LoadSceneAsync(address, LoadSceneMode.Additive);
            while (!sceneHandle.IsDone)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    await UnloadScene(sceneHandle, CancellationToken.None);
                    return MapContentLoadResult.Fail("地图加载已取消");
                }

                onProgress?.Invoke(Mathf.Clamp01(sceneHandle.Progress));
                await UniTask.Yield(cancellationToken);
            }

            onProgress?.Invoke(1f);
            if (sceneHandle.Status != EOperationStatus.Succeed)
            {
                var error = sceneHandle.LastError;
                await UnloadScene(sceneHandle, CancellationToken.None);
                return MapContentLoadResult.Fail($"地图场景加载失败：{error}");
            }

            var scene = sceneHandle.SceneObject;
            MapContext found = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                var contexts = root.GetComponentsInChildren<MapContext>(true);
                if (contexts.Length == 0)
                {
                    continue;
                }

                if (found != null || contexts.Length > 1)
                {
                    await UnloadScene(sceneHandle, CancellationToken.None);
                    return MapContentLoadResult.Fail("地图场景必须且只能包含一个 MapContext");
                }

                found = contexts[0];
            }

            if (found == null)
            {
                await UnloadScene(sceneHandle, CancellationToken.None);
                return MapContentLoadResult.Fail("地图场景缺少 MapContext");
            }

            if (!found.Validate(out var mapError))
            {
                await UnloadScene(sceneHandle, CancellationToken.None);
                return MapContentLoadResult.Fail($"地图上下文无效：{mapError}");
            }

            found.SetMapContentActive(false);
            return MapContentLoadResult.Ok(new YooMapContentHandle(mapId, found, sceneHandle));
        }

        private static async UniTask<MapContentUnloadResult> UnloadInternal(IMapContentHandle handle, CancellationToken cancellationToken)
        {
            if (handle is not YooMapContentHandle yooHandle || yooHandle.SceneHandle == null)
            {
                return MapContentUnloadResult.Ok();
            }

            var error = await UnloadScene(yooHandle.SceneHandle, cancellationToken);
            return string.IsNullOrWhiteSpace(error)
                ? MapContentUnloadResult.Ok()
                : MapContentUnloadResult.Fail(error);
        }

        private static async UniTask<string> UnloadScene(SceneHandle handle, CancellationToken cancellationToken)
        {
            if (handle == null || !handle.IsValid)
            {
                return string.Empty;
            }

            var operation = handle.UnloadAsync();
            while (!operation.IsDone)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return "地图卸载已取消";
                }

                await UniTask.Yield(cancellationToken);
            }

            return operation.Status == EOperationStatus.Succeed ? string.Empty : operation.Error;
        }

        private sealed class YooMapContentHandle : IMapContentHandle
        {
            public YooMapContentHandle(string mapId, MapContext context, SceneHandle sceneHandle)
            {
                MapId = mapId;
                Context = context;
                SceneHandle = sceneHandle;
            }

            public string MapId { get; }
            public MapContext Context { get; }
            public SceneHandle SceneHandle { get; }
            public bool IsValid => SceneHandle != null && SceneHandle.IsValid;
        }
    }
}
