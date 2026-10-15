using System.Reflection;
using QFramework;

namespace Game.Tests
{
    internal static class TestReset
    {
        public static void ResetArchitecture()
        {
            typeof(Architecture<Game>)
                .GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic)
                ?.SetValue(null, null);
            LifeCycle.ClearForTests();
        }
    }
}
