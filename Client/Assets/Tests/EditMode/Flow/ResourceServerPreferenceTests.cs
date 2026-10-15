using System;
using System.IO;
using Boot;
using NUnit.Framework;

namespace Game.Tests
{
    public sealed class ResourceServerPreferenceTests
    {
        private string root;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "AnotherDayResourceServer", Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }

        [TestCase("http://192.168.1.10:8000/", "http://192.168.1.10:8000")]
        [TestCase("https://cdn.example.com/game/", "https://cdn.example.com/game")]
        public void NormalizeAcceptsAbsoluteHttpAddressesAndRemovesTrailingSlash(string value, string expected)
        {
            Assert.That(ResourceServerPreference.TryNormalize(value, out string normalized), Is.True);
            Assert.That(normalized, Is.EqualTo(expected));
        }

        [TestCase("")]
        [TestCase("cdn.example.com/game")]
        [TestCase("ftp://cdn.example.com/game")]
        public void NormalizeRejectsMissingOrUnsupportedSchemes(string value)
        {
            Assert.That(ResourceServerPreference.TryNormalize(value, out _), Is.False);
        }

        [Test]
        public void SavedOverrideRoundTripsAndResetReturnsToNoOverride()
        {
            Assert.That(ResourceServerPreference.TrySave(
                " https://cdn.example.com/resources/ ",
                root,
                out string saved,
                out string error), Is.True, error);
            Assert.That(saved, Is.EqualTo("https://cdn.example.com/resources"));

            Assert.That(ResourceServerPreference.TryLoad(root, out string loaded, out error), Is.True, error);
            Assert.That(loaded, Is.EqualTo(saved));

            Assert.That(ResourceServerPreference.TrySave("relative/path", root, out _, out _), Is.False);
            Assert.That(ResourceServerPreference.TryLoad(root, out loaded, out error), Is.True, error);
            Assert.That(loaded, Is.EqualTo(saved));

            Assert.That(ResourceServerPreference.TryReset(root, out error), Is.True, error);
            Assert.That(ResourceServerPreference.TryLoad(root, out _, out _), Is.False);
        }
    }
}
