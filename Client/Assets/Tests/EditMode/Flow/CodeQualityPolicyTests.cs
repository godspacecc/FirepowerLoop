using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    public sealed class CodeQualityPolicyTests
    {
        private static readonly Regex ChineseRegex = new Regex(@"[\u4e00-\u9fff]", RegexOptions.Compiled);
        private static readonly Regex DiagnosticContextRegex = new Regex(
            @"(?:\b(?:Log|Debug)\.(?:Error|Warning|Debug|Log|LogWarning|LogError)|\b(?:[A-Za-z_][A-Za-z0-9_]*\.)+Fail|\bFail|\bnew\s+PlayerItemUseFinishedEvent)\s*\([\s\S]*?\)\s*;" +
            @"|\b(?:error|Error|reason|Reason)\s*(?:=|\?\?=)\s*(?:\$?@?""[^""]*""|[^;?]+\?\s*(?:\$?@?""[^""]*""|""[^""]*"")\s*:\s*(?:\$?@?""[^""]*""|""[^""]*""))\s*;" +
            @"|throw\s+new\s+(?:ArgumentException|InvalidOperationException|DirectoryNotFoundException|FileNotFoundException)\s*\([\s\S]*?\)\s*;" +
            @"|new\s+LocalizationValidationIssue\s*\([\s\S]*?\)\s*;",
            RegexOptions.Compiled | RegexOptions.Singleline);
        private static readonly Regex StringLiteralRegex = new Regex(
            @"(?:\$?@?""(?<text>(?:[^""{}]|""""|\{(?:[^""}]|""[^""]*"")*\})*)"")",
            RegexOptions.Compiled);
        private static readonly string[] ForbiddenCommentTemplates =
        {
            "核心运行数据契约，表达状态、快照或值对象语义，不直接执行业务流程。",
            "对应的系统流程，依赖 Game.Init 固定注册的模型、系统和工具协同工作。",
            "对应的工具能力，封装平台、文件、资源或 UI 辅助细节。"
        };

        [Test]
        public void RequiredArchitectureDependenciesDoNotUseSilentNullAccess()
        {
            var root = ProjectRoot();
            AssertFileDoesNotContain(root, "Assets/Game/Scripts/Command/Localization/SetLanguageCommand.cs", "GetSystem<SettingsSystem>()?.");
            AssertFileDoesNotContain(root, "Assets/Game/Scripts/Command/Settings/ApplyDisplaySettingsCommand.cs", "GetSystem<SettingsSystem>()?.");
            AssertFileDoesNotContain(root, "Assets/Game/Scripts/System/Settings/SettingsSystem.cs", "GetSystem<HotKeySystem>()?.");
            AssertFileDoesNotContain(root, "Assets/Game/Scripts/System/Map/MapSceneSystem.cs", "GetSystem<AudioSystem>()?.");

            string redPoint = Read(root, "Assets/Game/Scripts/Controller/UI/Home/RedPointView.cs");
            Assert.That(redPoint, Does.Not.Contain("GetSystem<RedPointSystem>()?."));
            Assert.That(redPoint, Does.Not.Contain("new RedPointState(nodeId, false, 0)"));
        }

        [Test]
        public void ResourceUpdateAndRedPointResponsibilitiesStayOneWay()
        {
            var root = ProjectRoot();
            var resourceUpdateSystem = Read(root, "Assets/Game/Scripts/System/ResourceUpdate/ResourceUpdateSystem.cs");
            Assert.That(resourceUpdateSystem, Does.Not.Contain("RedPointSystem"));
            Assert.That(resourceUpdateSystem, Does.Not.Contain("RedPointMod"));
            Assert.That(resourceUpdateSystem, Does.Not.Contain("RedPointIds"));
            Assert.That(resourceUpdateSystem, Does.Not.Contain("SetLeaf("));
            Assert.That(resourceUpdateSystem, Does.Not.Contain("MarkDirty("));

            var redPointSystem = Read(root, "Assets/Game/Scripts/System/UI/RedPointSystem.cs");
            Assert.That(redPointSystem, Does.Not.Contain("ResourceUpdateStatus"));
            Assert.That(redPointSystem, Does.Not.Contain("ResourceUpdateModel"));
            Assert.That(redPointSystem, Does.Not.Contain("ResourceUpdateChangedEvent"));

            var resourceUpdateMod = Read(root, "Assets/Game/Scripts/System/UI/ResourceUpdateRedPointMod.cs");
            Assert.That(resourceUpdateMod, Does.Contain("ResourceUpdateModel"));
            Assert.That(resourceUpdateMod, Does.Contain("ResourceUpdateChangedEvent"));
            Assert.That(resourceUpdateMod, Does.Contain("NotifyChange()"));
            Assert.That(resourceUpdateMod, Does.Not.Contain("CheckAsync("));
        }

        [Test]
        public void RedPointTypesHaveChineseResponsibilityComments()
        {
            var root = ProjectRoot();
            AssertTypeHasChineseResponsibilityComment(
                Read(root, "Assets/Game/Scripts/System/UI/RedPointMod.cs"),
                "RedPointMod");
            AssertTypeHasChineseResponsibilityComment(
                Read(root, "Assets/Game/Scripts/System/UI/RedPointMod.cs"),
                "RedPointGroupMod");
            AssertTypeHasChineseResponsibilityComment(
                Read(root, "Assets/Game/Scripts/System/UI/ResourceUpdateRedPointMod.cs"),
                "ResourceUpdateRedPointMod");
        }

        [Test]
        public void RedPointSystemOwnedDiagnosticsHaveChineseContext()
        {
            const string relative = "Assets/Game/Scripts/System/UI/RedPointSystem.cs";
            var failures = FindEnglishDiagnosticLiterals(Read(ProjectRoot(), relative), relative);
            Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures));
        }

        [Test]
        public void BatchTwoArchitectureContractsStayBehindPortsAndLifecycleInterfaces()
        {
            var root = ProjectRoot();
            var playerSystems = new[]
            {
                "Assets/Game/Scripts/System/Player/PlayerMoveSystem.cs",
                "Assets/Game/Scripts/System/Player/PlayerCameraSystem.cs",
                "Assets/Game/Scripts/System/Combat/CombatSystem.cs",
                "Assets/Game/Scripts/System/Combat/EnemySystem.cs",
                "Assets/Game/Scripts/System/Loot/LootSystem.cs"
            };

            foreach (var file in playerSystems)
            {
                var source = Read(root, file);
                Assert.That(source, Does.Not.Contain("PlayerView"), file);
                Assert.That(source, Does.Not.Contain("Camera.main"), file);
                Assert.That(source, Does.Not.Contain("PlayerCameraCtrl"), file);
                Assert.That(source, Does.Not.Contain("ViewCtrlRegistry"), file);
            }

            Assert.That(Read(root, "Assets/Game/Scripts/Controller/Base/IViewCtrl.cs"), Does.Contain("IDisposable"));
            Assert.That(Read(root, "Assets/Game/Scripts/Controller/Base/ViewCtrlRegistry.cs"), Does.Contain("DisposeAll"));
            Assert.That(Read(root, "Assets/Game/Scripts/Controller/Map/MapControllerResult.cs"), Does.Contain("MapControllerResult"));
            Assert.That(Read(root, "Assets/Game/Scripts/Controller/Map/MapPreparationContext.cs"), Does.Contain("MapInstanceId"));
            Assert.That(Read(root, "Assets/Game/Scripts/System/Map/MapSceneSystem.cs"), Does.Contain("PrepareControllers"));
        }

        [Test]
        public void GameRuntimeDiagnosticsHaveChineseContext()
        {
            AssertDiagnosticsHaveChineseContext("Assets/Game/Scripts");
        }

        [Test]
        public void BootDiagnosticsHaveChineseContext()
        {
            AssertDiagnosticsHaveChineseContext("Assets/Boot");
        }

        [Test]
        public void EditorDiagnosticsHaveChineseContext()
        {
            AssertDiagnosticsHaveChineseContext("Assets/Editor");
        }

        [Test]
        public void DiagnosticScannerPartitionsPreserveOriginalSourceCoverage()
        {
            var partitionRoots = new[] { "Assets/Game/Scripts", "Assets/Boot", "Assets/Editor" };
            Assert.That(partitionRoots, Is.EquivalentTo(new[] { "Assets/Game/Scripts", "Assets/Boot", "Assets/Editor" }));

            var partitioned = partitionRoots
                .SelectMany(ProjectOwnedSourceFiles)
                .Select(RelativePath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var original = ProjectOwnedSourceFiles()
                .Select(RelativePath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            Assert.That(partitioned, Is.EquivalentTo(original));
        }

        [Test]
        public void DiagnosticScannerCoversTypedInstanceMultilineAndIssueContexts()
        {
            string redSource =
                "return HotKeyRebindResult.Fail(\"empty binding path\");\n" +
                "return result.Fail(\n    \"server start failed\");\n" +
                "Log.Error(\n    $\"load failed: {error}\");\n" +
                "issues.Add(new LocalizationValidationIssue(Type, code, key, " +
                "\"Language table is missing this key.\"));";

            var redFailures = FindEnglishDiagnosticLiterals(redSource, "fixture.cs");
            Assert.That(redFailures, Has.Count.EqualTo(4), string.Join(Environment.NewLine, redFailures));

            string greenSource =
                "return HotKeyRebindResult.Fail(\"热键绑定路径为空\");\n" +
                "return result.Fail(\n    \"资源服启动失败\");\n" +
                "Log.Error(\n    $\"加载失败：{error}\");\n" +
                "issues.Add(new LocalizationValidationIssue(Type, code, key, " +
                "\"语言表缺少该键。\"));";

            Assert.That(FindEnglishDiagnosticLiterals(greenSource, "fixture.cs"), Is.Empty);
        }

        [Test]
        public void DiagnosticScannerCoversFallbackConditionalReasonReturnEventAndBareFailContexts()
        {
            const string red = "error ??= \"fallback failed\"; error = bad ? \"first failed\" : \"second failed\"; reason = \"reason failed\"; new PlayerItemUseFinishedEvent(1, false, 0, \"event failed\"); Fail(\"bare failed\"); error = bad ? \"true branch failed\" : existingError; error = bad ? existingError : \"false branch failed\"; private static string SampleError(bool first) { if (first) return \"return first failed\"; return \"return second failed\"; }";
            Assert.That(FindEnglishDiagnosticLiterals(red, "fixture.cs"), Has.Count.EqualTo(11));

            const string green = "error ??= \"回退失败\"; error = bad ? \"第一项失败\" : \"第二项失败\"; reason = \"原因失败\"; new PlayerItemUseFinishedEvent(1, false, 0, \"事件失败\"); Fail(\"裸失败\"); error = bad ? \"真分支失败\" : existingError; error = bad ? existingError : \"假分支失败\"; private static string SampleError(bool first) { if (first) return \"返回第一项失败\"; return \"返回第二项失败\"; } error = TryReleaseForCleanup(handle, \"ExtractionCtrl\", error); error = $\"{error}；运行时回滚{(ok ? \"成功\" : \"失败：\" + reason)}\"; reason = CraftFailureReason.None;";
            Assert.That(FindEnglishDiagnosticLiterals(green, "fixture.cs"), Is.Empty);
        }

        [Test]
        public void CoreTypesHaveChineseResponsibilityComments()
        {
            var failures = new List<string>();
            foreach (string file in CommentRequiredFiles())
            {
                string relative = RelativePath(file);
                string source = File.ReadAllText(file);
                foreach (string template in ForbiddenCommentTemplates)
                {
                    if (source.Contains(template))
                    {
                        failures.Add($"{relative}: 仍包含模板化中文注释片段：\"{template}\"");
                    }
                }

                foreach (Match declaration in Regex.Matches(source, @"(?m)^\s*(?:public|internal)\s+(?:sealed\s+|static\s+|readonly\s+|abstract\s+|partial\s+)*?(?:class|struct|interface|enum)\s+([A-Za-z_][A-Za-z0-9_]*)"))
                {
                    string nearby = PreviousLines(source, declaration.Index, 12);
                    if (!Regex.IsMatch(nearby, @"///\s*<summary>[\s\S]*?[\u4e00-\u9fff][\s\S]*?</summary>"))
                    {
                        failures.Add($"{relative}: {declaration.Groups[1].Value} 缺少中文 XML 类级说明");
                    }
                }
            }

            Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures.Take(120)));
        }

        [Test]
        public void KeyWorkflowMethodsHaveChineseComments()
        {
            var required = new[]
            {
                ("Assets/Game/Scripts/System/Settings/SettingsSystem.cs", "LoadAndApply"),
                ("Assets/Game/Scripts/System/Settings/SettingsSystem.cs", "SaveCurrent"),
                ("Assets/Game/Scripts/System/Save/SaveSystem.cs", "PrepareBase"),
                ("Assets/Game/Scripts/System/Save/SaveSystem.cs", "CommitBaseRuntime"),
                ("Assets/Game/Scripts/System/Save/SaveSystem.cs", "PrepareExpedition"),
                ("Assets/Game/Scripts/System/Save/SaveSystem.cs", "CommitExpeditionRuntime"),
                ("Assets/Game/Scripts/System/Save/SaveSystem.cs", "RestoreRuntimeSnapshot"),
                ("Assets/Game/Scripts/System/Map/MapSceneSystem.cs", "LoadMapAsync"),
                ("Assets/Game/Scripts/System/Map/MapSceneSystem.cs", "UnloadCurrentMapAsync"),
                ("Assets/Game/Scripts/System/Application/GameSessionFlowSystem.cs", "EnterBaseAsync"),
                ("Assets/Game/Scripts/System/Expedition/ExpeditionFlowSystem.cs", "ResumePendingExpeditionAsync"),
                ("Assets/Game/Scripts/System/Audio/AudioSystem.cs", "PlaySound"),
                ("Assets/Game/Scripts/System/Audio/AudioSystem.cs", "PlayMusic"),
                ("Assets/Game/Scripts/System/ResourceUpdate/ResourceUpdateSystem.cs", "CheckAsync"),
                ("Assets/Editor/Build/BuildUtility.cs", "RunWorkflow"),
                ("Assets/Editor/Build/BuildUtility.cs", "BuildPlayer"),
                ("Assets/Editor/Build/HybridCLRBuildUtility.cs", "CompileHotUpdateDll"),
                ("Assets/Editor/YooAsset/YooAssetBuildUtility.cs", "BuildDefaultPackage"),
                ("Assets/Editor/YooAsset/LocalResourceServerUtility.cs", "StartAndProbe")
            };

            var failures = required
                .Where(item => !HasChineseMethodSummary(Read(ProjectRoot(), item.Item1), item.Item2))
                .Select(item => $"{item.Item1}: {item.Item2} 缺少中文 XML 方法说明")
                .ToList();

            Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures));
        }

        private readonly struct DiagnosticCandidate
        {
            public DiagnosticCandidate(string text, int index, int length) { Text = text; Index = index; Length = length; }
            public string Text { get; }
            public int Index { get; }
            public int Length { get; }
        }

        private static IReadOnlyList<string> FindEnglishDiagnosticLiterals(string source, string relative)
        {
            return CollectDiagnosticCandidates(source)
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Text) && !ChineseRegex.IsMatch(candidate.Text))
                .Select(candidate => $"{relative}: 缺少中文诊断上下文：\"{candidate.Text}\"")
                .ToList();
        }

        private static IReadOnlyList<DiagnosticCandidate> CollectDiagnosticCandidates(string source)
        {
            var candidates = new List<DiagnosticCandidate>();
            var seen = new HashSet<(int, int)>();
            void Add(Match literal, int offset)
            {
                int index = offset + literal.Index;
                if (seen.Add((index, literal.Length))) candidates.Add(new DiagnosticCandidate(literal.Groups["text"].Value, index, literal.Length));
            }
            foreach (Match context in DiagnosticContextRegex.Matches(source))
            {
                foreach (Match literal in StringLiteralRegex.Matches(context.Value))
                {
                    Add(literal, context.Index);
                }
            }

            foreach (Match method in Regex.Matches(source, @"\bstring\s+\w+(?:Error|Reason|Message)\s*\([^)]*\)\s*\{"))
            {
                string body = ExtractMethodBody(source, method.Index + method.Length - 1);
                foreach (Match returned in Regex.Matches(body, @"\breturn\s+(?<literal>\$?""[^""]*"")\s*;"))
                {
                    Match literal = StringLiteralRegex.Match(returned.Groups["literal"].Value);
                    Add(literal, method.Index + method.Length - 1 + body.IndexOf(returned.Groups["literal"].Value, StringComparison.Ordinal));
                }
            }

            foreach (Match conditional in Regex.Matches(source, @"\b(?:error|Error|reason|Reason)\s*=\s*[^;?]+\?\s*(?:(?<literal>\$?""[^""]*"")\s*:\s*[^;]+|[^:;]+:\s*(?<literal>\$?""[^""]*""))\s*;"))
            {
                foreach (Capture literal in conditional.Groups["literal"].Captures)
                {
                    Match matched = StringLiteralRegex.Match(literal.Value);
                    Add(matched, conditional.Index + literal.Index);
                }
            }
            return candidates;
        }

        private static string ExtractMethodBody(string source, int openingBrace)
        {
            int depth = 0;
            bool inString = false, inChar = false, lineComment = false, blockComment = false, escaped = false;
            for (int index = openingBrace; index < source.Length; index++)
            {
                char current = source[index];
                char next = index + 1 < source.Length ? source[index + 1] : '\0';
                if (lineComment) { if (current == '\n') lineComment = false; continue; }
                if (blockComment) { if (current == '*' && next == '/') { blockComment = false; index++; } continue; }
                if (!inString && !inChar && current == '/' && next == '/') { lineComment = true; index++; continue; }
                if (!inString && !inChar && current == '/' && next == '*') { blockComment = true; index++; continue; }
                if (inString) { if (!escaped && current == '"') inString = false; escaped = !escaped && current == '\\'; continue; }
                if (inChar) { if (!escaped && current == '\'') inChar = false; escaped = !escaped && current == '\\'; continue; }
                if (current == '"') { inString = true; escaped = false; continue; }
                if (current == '\'') { inChar = true; escaped = false; continue; }
                if (current == '{') depth++;
                if (current == '}' && --depth == 0) return source.Substring(openingBrace + 1, index - openingBrace - 1);
            }
            return string.Empty;
        }

        private static void AssertDiagnosticsHaveChineseContext(string relativeRoot)
        {
            var failures = new List<string>();
            foreach (string file in ProjectOwnedSourceFiles(relativeRoot))
            {
                string relative = RelativePath(file);
                failures.AddRange(FindEnglishDiagnosticLiterals(File.ReadAllText(file), relative));
            }

            Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures.Take(80)));
        }

        private static bool HasChineseMethodSummary(string source, string methodName)
        {
            var match = Regex.Match(source, $@"(?m)^\s*(?:public|internal|private)\s+(?:static\s+|async\s+)*[\w<>,\s\[\]\?]+\s+{Regex.Escape(methodName)}\s*\(");
            if (!match.Success)
                return false;

            string nearby = PreviousLines(source, match.Index, 12);
            return Regex.IsMatch(nearby, @"///\s*<summary>[\s\S]*?[\u4e00-\u9fff][\s\S]*?</summary>");
        }

        private static void AssertTypeHasChineseResponsibilityComment(string source, string typeName)
        {
            var declaration = Regex.Match(
                source,
                $@"(?m)^\s*(?:public|internal)\s+(?:sealed\s+|static\s+|readonly\s+|abstract\s+|partial\s+)*?(?:class|struct|interface|enum)\s+{Regex.Escape(typeName)}\b");
            Assert.That(declaration.Success, Is.True, $"未找到类型：{typeName}");
            Assert.That(
                Regex.IsMatch(PreviousLines(source, declaration.Index, 12), @"///\s*<summary>[\s\S]*?[\u4e00-\u9fff][\s\S]*?</summary>"),
                Is.True,
                $"{typeName} 缺少中文 XML 类级说明");
        }

        private static string PreviousLines(string source, int index, int lineCount)
        {
            string before = source.Substring(0, index).TrimEnd();
            var lines = Regex.Split(before, "\r?\n");
            return string.Join("\n", lines.Skip(Math.Max(0, lines.Length - lineCount)));
        }

        private static IEnumerable<string> ProjectOwnedSourceFiles()
        {
            return new[] { "Assets/Game/Scripts", "Assets/Boot", "Assets/Editor" }
                .SelectMany(ProjectOwnedSourceFiles);
        }

        private static IEnumerable<string> ProjectOwnedSourceFiles(string relativeRoot)
        {
            string path = Path.Combine(ProjectRoot(), relativeRoot.Replace('/', Path.DirectorySeparatorChar));
            return Directory.Exists(path)
                ? Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories).Where(file => !IsExcluded(RelativePath(file)))
                : Enumerable.Empty<string>();
        }

        private static IEnumerable<string> CommentRequiredFiles()
        {
            string root = ProjectRoot();
            var explicitFiles = new[]
            {
                "Assets/Game/Scripts/Game.cs",
                "Assets/Game/Scripts/GameStart.cs",
                "Assets/Editor/Build/BuildUtility.cs",
                "Assets/Editor/Build/HybridCLRBuildUtility.cs",
                "Assets/Editor/Build/BuildStageTiming.cs",
                "Assets/Editor/Build/BuildStateStore.cs",
                "Assets/Editor/YooAsset/YooAssetBuildUtility.cs",
                "Assets/Editor/YooAsset/LocalResourceServerUtility.cs"
            }.Select(path => Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));

            var directories = new[]
            {
                "Assets/Boot",
                "Assets/Game/Scripts/Model",
                "Assets/Game/Scripts/System",
                "Assets/Game/Scripts/Utility"
            };

            return explicitFiles.Concat(directories
                    .Select(path => Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)))
                    .Where(Directory.Exists)
                    .SelectMany(path => Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories)))
                .Where(File.Exists)
                .Where(file => !IsExcluded(RelativePath(file)))
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static bool IsExcluded(string relative)
        {
            string path = relative.Replace('\\', '/');
            return path.Contains("/Framework/") ||
                   path.Contains("/ThirdParty/") ||
                   path.EndsWith("/Editor/LauncherSetup.cs", StringComparison.OrdinalIgnoreCase) ||
                   path.Contains("/Utility/UI/LoopScrollRect/") ||
                   path.Contains("/Network/Protocol/Generated/") ||
                   path.Contains("/Model/Config/") ||
                   path.EndsWith("/Utility/Localization/TextsGenerated.cs", StringComparison.OrdinalIgnoreCase) ||
                   path.Contains("/Editor/UI/LoopScroll/") ||
                   path.Contains("/Assets/Tests/");
        }

        private static void AssertFileDoesNotContain(string root, string relative, string text)
        {
            Assert.That(Read(root, relative), Does.Not.Contain(text), relative);
        }

        private static string Read(string root, string relative)
        {
            return File.ReadAllText(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static string ProjectRoot()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }

        private static string RelativePath(string file)
        {
            return file.Substring(ProjectRoot().Length + 1).Replace('\\', '/');
        }
    }
}
