using QFramework;
using UnityEngine;
using Cysharp.Threading.Tasks;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Game
{
    public sealed class QuitGameCommand : AbstractCommand
    {
        private static System.Action quitHandlerForTests;
        /// <summary>菜单退出与窗口关闭共用同一等待，避免重复退出或提前卸载架构。</summary>
        private static IArchitecture waitingArchitecture;

        public static bool TryPrepareQuit(IArchitecture architecture)
        {
            var runs = architecture?.GetSystem<ArcadeRunSystem>();
            if (runs?.IsSavingResult != true) return true;
            if (!ReferenceEquals(waitingArchitecture, architecture))
            {
                waitingArchitecture = architecture;
                ResumeQuitAfterResultAsync(architecture, runs).Forget();
            }
            return false;
        }

        /// <summary>保存失败取消这次退出，让玩家保留原局及错误提示；后续明确退出仍按正常入口处理。</summary>
        private static async UniTaskVoid ResumeQuitAfterResultAsync(IArchitecture architecture, ArcadeRunSystem runs)
        {
            bool saved = await runs.WaitForPendingResultSaveAsync();
            if (!ReferenceEquals(waitingArchitecture, architecture)) return;
            waitingArchitecture = null;
            if (saved) architecture.SendCommand(new QuitGameCommand());
        }

        public static void SetQuitHandlerForTests(System.Action handler)
        {
            quitHandlerForTests = handler;
        }

        protected override void OnExecute()
        {
            if (!TryPrepareQuit(((IBelongToArchitecture)this).GetArchitecture())) return;
            if (quitHandlerForTests != null)
            {
                quitHandlerForTests.Invoke();
                return;
            }

#if UNITY_EDITOR
            EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
