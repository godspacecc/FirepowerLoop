using QFramework;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Game
{
    public sealed class QuitGameCommand : AbstractCommand
    {
        private static System.Action quitHandlerForTests;
        /// <summary>菜单退出与关闭窗口都先保存当前幸存者结果。</summary>
        public static bool TryPrepareQuit(IArchitecture architecture)
        {
            return architecture?.GetModel<SurvivorModel>()?.Snapshot.Active != true ||
                   architecture.GetSystem<SurvivorSystem>().TryFinish(out _);
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
