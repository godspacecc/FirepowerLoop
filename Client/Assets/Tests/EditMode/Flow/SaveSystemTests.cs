using System.Linq;
using NUnit.Framework;

namespace Game.Tests
{
    public sealed class SaveSystemTests
    {
        [Test]
        public void PublicRuntimeApi_UsesFixedDomainOperationsOnly()
        {
            var methods = typeof(SaveSystem).GetMethods().Select(x => x.Name).ToArray();
            CollectionAssert.DoesNotContain(methods, "CreateSlot");
            CollectionAssert.DoesNotContain(methods, "PrepareLoad");
            CollectionAssert.DoesNotContain(methods, "CommitPreparedSave");
            CollectionAssert.DoesNotContain(methods, "GetSlots");
            CollectionAssert.DoesNotContain(methods, "ClearAll");
            CollectionAssert.Contains(methods, "InspectBase");
            CollectionAssert.Contains(methods, "InspectExpedition");
            CollectionAssert.Contains(methods, "DeleteExpeditionFiles");
        }
    }
}
