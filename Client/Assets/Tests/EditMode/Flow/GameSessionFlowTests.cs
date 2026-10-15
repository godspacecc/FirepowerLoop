using System.Linq;
using NUnit.Framework;

namespace Game.Tests
{
    public sealed class GameSessionFlowTests
    {
        [Test]
        public void GameSessionFlow_OnlyExposesBaseEntry()
        {
            var names = typeof(GameSessionFlowSystem).GetMethods().Select(method => method.Name).ToArray();
            CollectionAssert.Contains(names, "EnterBaseAsync");
            CollectionAssert.DoesNotContain(names, "EnterSaveAsync");
            CollectionAssert.DoesNotContain(names, "ReturnToMainMenuAsync");
        }
    }
}
