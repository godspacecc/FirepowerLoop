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

        public static void SetQuitHandlerForTests(System.Action handler)
        {
            quitHandlerForTests = handler;
        }

        protected override void OnExecute()
        {


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
