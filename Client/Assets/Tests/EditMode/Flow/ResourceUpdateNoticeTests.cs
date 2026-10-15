using System.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    public sealed class ResourceUpdateNoticeTests
    {
        [Test]
        public void RejectsInvalidSchemaAndOversizedDocuments()
        {
            AssertInvalid("{\"schemaVersion\":2,\"defaultLanguage\":\"zh-CN\",\"notices\":[]}", "schemaVersion");

            var oversized = "{\"schemaVersion\":1,\"defaultLanguage\":\"zh-CN\",\"notices\":[],\"padding\":\"" +
                new string('a', ResourceUpdateNoticeDocument.MaxBytes) +
                "\"}";
            AssertInvalid(oversized, "256 KiB");
        }

        [Test]
        public void RejectsTooManyRecordsInvalidVersionsAndDuplicates()
        {
            var tooMany = "{\"schemaVersion\":1,\"defaultLanguage\":\"zh-CN\",\"notices\":[" +
                string.Join(",", Enumerable.Range(0, 101).Select(i => Notice($"20260723{i % 24:00}{i % 60:00}"))) +
                "]}";
            AssertInvalid(tooMany, "count");

            AssertInvalid(Document(Notice("bad-version")), "version");
            AssertInvalid(Document(Notice("202607231230") + "," + Notice("202607231230")), "duplicate");
        }

        [Test]
        public void SelectsOnlyEnabledExactVersion()
        {
            var json = Document(
                Notice("202607231200", enabled: false) + "," +
                Notice("202607231230", title: "Version update", summary: "Ready"));
            var parse = ResourceUpdateNoticeDocument.TryParse(json, out var document);
            Assert.IsTrue(parse.Success, parse.Error);

            Assert.IsFalse(ResourceUpdateNoticeDocument.Select(document, "202607231200", "en-US").HasNotice);
            var selected = ResourceUpdateNoticeDocument.Select(document, "202607231230", "en-US");
            Assert.IsTrue(selected.HasNotice);
            Assert.That(selected.Content.Title, Is.EqualTo("Version update"));
        }

        [Test]
        public void FallsBackOnlyToDefaultLanguage()
        {
            var json = "{\"schemaVersion\":1,\"defaultLanguage\":\"zh-CN\",\"notices\":[{" +
                "\"version\":\"202607231230\",\"enabled\":true,\"texts\":{" +
                "\"en-US\":{\"title\":\"\",\"summary\":\"\",\"items\":[]}," +
                "\"zh-CN\":{\"title\":\"默认\",\"summary\":\"摘要\",\"items\":[]}," +
                "\"ja-JP\":{\"title\":\"third\",\"summary\":\"third\",\"items\":[]}" +
                "}}]}";
            var parse = ResourceUpdateNoticeDocument.TryParse(json, out var document);
            Assert.IsTrue(parse.Success, parse.Error);

            var selected = ResourceUpdateNoticeDocument.Select(document, "202607231230", "en-US");
            Assert.IsTrue(selected.HasNotice);
            Assert.That(selected.Content.Title, Is.EqualTo("默认"));

            selected = ResourceUpdateNoticeDocument.Select(document, "202607231230", "fr-FR");
            Assert.That(selected.Content.Title, Is.EqualTo("默认"));
        }

        [Test]
        public void DoesNotUseThirdLanguageWhenDefaultIsEmpty()
        {
            var json = "{\"schemaVersion\":1,\"defaultLanguage\":\"zh-CN\",\"notices\":[{" +
                "\"version\":\"202607231230\",\"enabled\":false,\"texts\":{" +
                "\"zh-CN\":{\"title\":\"\",\"summary\":\"\",\"items\":[]}," +
                "\"ja-JP\":{\"title\":\"third\",\"summary\":\"third\",\"items\":[]}" +
                "}}]}";
            var parse = ResourceUpdateNoticeDocument.TryParse(json, out var document);
            Assert.IsTrue(parse.Success, parse.Error);

            var selected = ResourceUpdateNoticeDocument.Select(document, "202607231230", "en-US");
            Assert.IsFalse(selected.HasNotice);
        }

        [Test]
        public void RejectsControlBomAndRichText()
        {
            AssertInvalid(Document(Notice("202607231230", title: "bad\u0001")), "control");
            AssertInvalid(Document(Notice("202607231230", title: "\uFEFFbad")), "BOM");
            AssertInvalid(Document(Notice("202607231230", title: "<color=red>bad</color>")), "rich text");
            AssertInvalid(Document(Notice("202607231230", title: "<size=20>bad</size>")), "rich text");
            AssertInvalid(Document(Notice("202607231230", title: "<link=\"x\">bad</link>")), "rich text");
        }

        [Test]
        public void UnicodeLengthCountsSurrogatePairAsOneCodePoint()
        {
            var title = new string('a', 99) + char.ConvertFromUtf32(0x1F600);
            var parse = ResourceUpdateNoticeDocument.TryParse(Document(Notice("202607231230", title: title)), out _);
            Assert.IsTrue(parse.Success, parse.Error);

            parse = ResourceUpdateNoticeDocument.TryParse(Document(Notice("202607231230", title: title + "b")), out _);
            Assert.IsFalse(parse.Success);
        }

        [Test]
        public void FormatsItemsAsPlainNumberedTextWithoutExtraBlankLines()
        {
            var content = new ResourceUpdateNoticeContent(
                "202607231230",
                "Title",
                "",
                new[] { "First", "", "Second" });

            Assert.That(ResourceUpdateNoticeDocument.FormatForNotify(content, false), Is.EqualTo("1. First\n2. Second"));

            content = new ResourceUpdateNoticeContent("202607231230", "Title", "Summary", new[] { "First", "Second" });
            Assert.That(ResourceUpdateNoticeDocument.FormatForNotify(content, false), Is.EqualTo("Summary\n1. First\n2. Second"));
            Assert.That(ResourceUpdateNoticeDocument.FormatForNotify(content, true), Is.EqualTo("Title\nSummary\n1. First\n2. Second"));
        }

        [Test]
        public void CSharpAndPowerShellValidationAgreeOnStrictJsonSamples()
        {
            var validEmpty = "{\"schemaVersion\":1,\"defaultLanguage\":\"zh-CN\",\"notices\":[]}";
            var validMulti = Document(Notice("202607231729", title: "Title", summary: "Summary"));
            var samples = new Dictionary<string, bool>
            {
                [validEmpty] = true,
                [validMulti] = true,
                ["{\"schemaVersion\":1,\"defaultLanguage\":\"zh-CN\",\"notices\":{}}"] = false,
                ["{\"schemaVersion\":1,\"defaultLanguage\":\"zh-CN\",\"notices\":[{\"version\":\"202607231729\",\"enabled\":true,\"texts\":[]}]}"] = false,
                [Document("{\"version\":\"202607231729\",\"enabled\":true,\"texts\":\"bad\"}")] = false,
                [Document("{\"version\":\"202607231729\",\"enabled\":true,\"texts\":{\"zh-CN\":{\"title\":1,\"summary\":\"S\",\"items\":[]}}}")] = false,
                [Document("{\"version\":\"202607231729\",\"enabled\":true,\"texts\":{\"zh-CN\":{\"title\":\"T\",\"summary\":true,\"items\":[]}}}")] = false,
                [Document("{\"version\":\"202607231729\",\"enabled\":true,\"texts\":{\"zh-CN\":{\"title\":{},\"summary\":\"S\",\"items\":[]}}}")] = false,
                [Document("{\"version\":\"202607231729\",\"enabled\":true,\"texts\":{\"zh-CN\":{\"title\":\"T\",\"summary\":\"S\",\"items\":1}}}")] = false,
                [Document("{\"version\":\"202607231729\",\"enabled\":true,\"texts\":{\"zh-CN\":{\"title\":\"T\",\"summary\":\"S\",\"items\":[1]}}}")] = false,
                [Document(Notice("202607231729", title: "<anything>"))] = false,
                [Document(Notice("202607231729", title: "line\nbreak"))] = false,
                [Document(Notice("202607231729", title: "tab\tbreak"))] = false,
                [Document(Notice("202607231729", title: "bad\u0001"))] = false,
            };

            var script = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Server", "ResourceServer", "validate_resource_update_notices.ps1"));
            var temp = Path.Combine(Path.GetTempPath(), "AnotherDayNoticeValidation", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                var index = 0;
                foreach (var sample in samples)
                {
                    var csharp = ResourceUpdateNoticeDocument.TryParse(sample.Key, out _).Success;
                    var path = Path.Combine(temp, $"sample-{index++}.json");
                    File.WriteAllText(path, sample.Key);
                    var powershell = RunPowerShellValidator(script, path) == 0;
                    Assert.That(csharp, Is.EqualTo(sample.Value), sample.Key);
                    Assert.That(powershell, Is.EqualTo(sample.Value), sample.Key);
                    Assert.That(powershell, Is.EqualTo(csharp), sample.Key);
                }
            }
            finally
            {
                Directory.Delete(temp, true);
            }
        }

        private static void AssertInvalid(string json, string expected)
        {
            var result = ResourceUpdateNoticeDocument.TryParse(json, out _);
            Assert.IsFalse(result.Success);
            Assert.That(result.Error, Does.Contain(expected));
        }

        private static string Document(string notices)
        {
            return "{\"schemaVersion\":1,\"defaultLanguage\":\"zh-CN\",\"notices\":[" + notices + "]}";
        }

        private static string Notice(
            string version,
            bool enabled = true,
            string title = "Title",
            string summary = "Summary")
        {
            return "{\"version\":\"" + version + "\",\"enabled\":" + enabled.ToString().ToLowerInvariant() +
                ",\"texts\":{\"zh-CN\":{\"title\":" + JsonConvert.ToString(title) + ",\"summary\":" + JsonConvert.ToString(summary) +
                ",\"items\":[\"Item\"]},\"en-US\":{\"title\":\"Version update\",\"summary\":\"Ready\",\"items\":[\"One\"]}}}";
        }

        private static int RunPowerShellValidator(string script, string path)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoLogo -NoProfile -ExecutionPolicy Bypass -File \"{script}\" -Path \"{path}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (var process = Process.Start(startInfo))
            {
                Assert.That(process, Is.Not.Null);
                process.WaitForExit(10000);
                return process.ExitCode;
            }
        }
    }
}
