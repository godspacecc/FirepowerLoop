using System;
using UnityEditor;
using UnityEditor.Build;

namespace Shared.UnityTools
{
    /// <summary>统一热更菜单名称和错误反馈。项目适配器按平台读取既有记录并执行本地发布。</summary>
    public static class HotUpdateMenu
    {
        public const string Android = "Tools/打包/Android/构建热更新（代码+资源）";
        public const string Pc = "Tools/打包/PC/构建热更新（代码+资源）";

        /// <summary>读取该平台最近记录后构建并发布。加载或校验失败时终止操作。</summary>
        public static void BuildAndPublish<T>(BuildTarget target, Func<T> readLatest, Action<T> buildAndPublish) =>
            BuildAndPublish(target, () => buildAndPublish(readLatest()));

        /// <summary>调用已有的按平台选包、构建和发布事务，补充统一的菜单失败反馈。</summary>
        public static void BuildAndPublish(BuildTarget target, Action buildLatestAndPublish)
        {
            try
            {
                buildLatestAndPublish();
            }
            catch (Exception error)
            {
                throw new BuildFailedException(new InvalidOperationException(
                    $"{target} 热更新构建或本地发布失败。{error.GetBaseException().Message}", error));
            }
        }
    }
}
