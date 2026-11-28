using System;
using UnityEditor;
using UnityEngine;

namespace Shared.UnityTools
{
    /// <summary>统一快速打包入口和确认提示。各工程负责验证快照并生成对应平台的安装包。</summary>
    internal static class QuickBuildMenu
    {
        internal const string AndroidAot = "Tools/打包/Android/快速打包/复用 AOT 打包";
        internal const string AndroidAll = "Tools/打包/Android/快速打包/复用全部打包";
        internal const string PcAot = "Tools/打包/PC/快速打包/复用 AOT 打包";
        internal const string PcAll = "Tools/打包/PC/快速打包/复用全部打包";

        internal static void ConfirmAndBuild(bool reuseAll, Action build)
        {
            string title = reuseAll ? "复用全部打包" : "复用 AOT 打包";
            string message = reuseAll
                ? "沿用上次准备的包型、玩法代码、AOT 和资源，只重新生成安装包。输入发生变化时会停止。"
                : "沿用上次准备的包型和 AOT，重新编译玩法代码及构建资源，再生成安装包。";
            if (Application.isBatchMode || EditorUtility.DisplayDialog(title, message, "开始打包", "取消"))
                build();
        }
    }
}
