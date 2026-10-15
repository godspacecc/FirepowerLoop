using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    public sealed class UiCodingRuleTests
    {
        private const string ControllerRoot = "Assets/Game/Scripts/Controller";

        private static readonly FieldInfo[] RequiredBindingFields = typeof(BaseUI).Assembly.GetTypes()
            .Where(type => typeof(BaseUI).IsAssignableFrom(type) || typeof(BaseBoxUI).IsAssignableFrom(type))
            .SelectMany(type => type.GetFields(BindingFlags.Instance | BindingFlags.Public))
            .Where(field => typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType) && !field.IsNotSerialized)
            .ToArray();

        private static readonly HashSet<string> RequiredBindingTypeNames = new HashSet<string>(
            RequiredBindingFields
                .Where(field => !typeof(BaseUI).IsAssignableFrom(field.FieldType) &&
                    !typeof(BaseBoxUI).IsAssignableFrom(field.FieldType))
                .SelectMany(field => new[] { field.FieldType.Name, field.FieldType.FullName })
                .Where(name => !string.IsNullOrEmpty(name)));

        private static readonly string RequiredBindingField = string.Join("|",
            RequiredBindingFields
                .Select(field => Regex.Escape(field.Name))
                .Distinct()
                .OrderByDescending(name => name.Length));

        private static readonly Regex RequiredBindingNullConditional = new Regex(
            $@"\b[A-Za-z_]\w*\s*\.\s*(?:{RequiredBindingField})\s*\?\.",
            RegexOptions.Compiled);

        private static readonly Regex RequiredBindingNullComparison = new Regex(
            $@"(?:\b[A-Za-z_]\w*\s*\.\s*(?:{RequiredBindingField})\s*(?:==|!=)\s*null|\bnull\s*(?:==|!=)\s*[A-Za-z_]\w*\s*\.\s*(?:{RequiredBindingField}))",
            RegexOptions.Compiled);

        private static readonly Regex MethodDeclaration = new Regex(
            @"\b(?:public|private|protected|internal)\s+(?:(?:static|virtual|override|async|sealed|new|partial)\s+)*(?:[\w:<>,\.\[\]\?]+\s+)+[A-Za-z_]\w*\s*\((?<parameters>[^)]*)\)\s*\{",
            RegexOptions.Compiled);

        private static readonly Regex AliasAssignment = new Regex(
            $@"\b(?:var|(?:global::)?[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)\s+(?<alias>[A-Za-z_]\w*)\s*=\s*[A-Za-z_]\w*\s*\.\s*(?:{RequiredBindingField})\s*;",
            RegexOptions.Compiled);

        private static readonly Regex InlineEventRegistration = new Regex(
            @"(?:AddListener|RegisterEvent\s*<[^>]+>|OnClickEvent|OnPointerClickEvent)\s*\(\s*(?:\([^)]*\)|[A-Za-z_]\w*)\s*=>",
            RegexOptions.Compiled | RegexOptions.Singleline);

        private static readonly Regex HardcodedPlayerMessage = new Regex(
            @"(?:\bShowError|Notify\.ShowFloatTips)\s*\(\s*""|Notify\.ShowMessage\s*\(\s*""|Notify\.ShowMessage\s*\(\s*[^,]+,\s*""",
            RegexOptions.Compiled | RegexOptions.Singleline);

        private static readonly Regex ForwardedTechnicalError = new Regex(
            @"(?:\bShowError|Notify\.ShowFloatTips)\s*\(\s*(?:[A-Za-z_]\w*\.)?Error\s*\)|(?:\bShowError|Notify\.ShowFloatTips)\s*\(\s*(?:exception\.Message|error)\s*\)|Notify\.ShowMessage\s*\([^\n;]*(?:[A-Za-z_]\w*\.Error|exception\.Message|\berror\b)",
            RegexOptions.Compiled | RegexOptions.Singleline);

        [Test]
        public void RequiredUiBindings_DoNotUseSilentNullGuards()
        {
            var violations = Scan(RequiredBindingNullConditional);
            violations.AddRange(Scan(RequiredBindingNullComparison));
            foreach (var path in Directory.GetFiles(ControllerRoot, "*.cs", SearchOption.AllDirectories))
            {
                var source = File.ReadAllText(path);
                violations.AddRange(FindRequiredUiParameterNullGuards(source, path));
                violations.AddRange(FindRequiredUiAliasNullGuards(source, path));
            }

            violations = violations.Distinct().ToList();
            Assert.That(violations, Is.Empty, string.Join("\n", violations));
        }

        [Test]
        public void InventoryControllers_DoNotCreateOrPoolSlotViews()
        {
            var files = new[]
            {
                "Assets/Game/Scripts/Controller/UICtrl/Inventory/BagUICtrl.cs",
                "Assets/Game/Scripts/Controller/UICtrl/Inventory/WarehouseUICtrl.cs",
                "Assets/Game/Scripts/Controller/UICtrl/Inventory/SearchContainerUICtrl.cs"
            };

            foreach (var path in files)
            {
                var source = File.ReadAllText(path);
                Assert.That(source, Does.Not.Contain("Instantiate("), path);
                Assert.That(source, Does.Not.Contain("Destroy("), path);
                Assert.That(source, Does.Not.Contain("DestroyImmediate("), path);
                Assert.That(source, Does.Not.Contain("ObjectPool"), path);
                Assert.That(source, Does.Not.Contain("SlotTemplate"), path);
                Assert.That(source, Does.Not.Contain("ExpansionSlotBox"), path);
            }
        }

        [Test]
        public void InventorySlotBoxRequiredBindings_DoNotUseSilentNullGuards()
        {
            var files = new[]
            {
                "Assets/Game/Scripts/Controller/UICtrl/Inventory/BagUICtrl.cs",
                "Assets/Game/Scripts/Controller/UICtrl/Inventory/WarehouseUICtrl.cs",
                "Assets/Game/Scripts/Controller/UICtrl/Inventory/SearchContainerUICtrl.cs",
                "Assets/Game/Scripts/Controller/UICtrl/Inventory/InventorySlotBoxReuse.cs"
            };
            var fields = new[]
            {
                "ItemContentRoot", "ExpansionContentRoot", "ClickButton", "ExpansionButton",
                "IconImage", "AmountText", "ExpansionLabelText", "EmptyIconRoot",
                "SelectedFrame", "DisabledOverlay", "DragFeedback"
            };
            var fieldPattern = string.Join("|", fields.Select(Regex.Escape));
            var nullGuard = new Regex(
                $@"\.\s*(?:{fieldPattern})\s*\?\.|(?:\.\s*(?:{fieldPattern})\s*(?:==|!=)\s*null|null\s*(?:==|!=)\s*\w+\.\s*(?:{fieldPattern}))",
                RegexOptions.Compiled);

            foreach (var path in files)
            {
                Assert.That(nullGuard.IsMatch(File.ReadAllText(path)), Is.False, path);
            }
        }

        [Test]
        public void UiControllers_DoNotRegisterEventsWithInlineLambdas()
        {
            var violations = Scan(InlineEventRegistration);
            Assert.That(violations, Is.Empty, string.Join("\n", violations));
        }

        [Test]
        public void TestCmdController_UsesStaticResultStructureAndRuntimeRowEventAdapter()
        {
            var source = File.ReadAllText("Assets/Game/Scripts/Controller/UICtrl/Diagnostics/TestCmdUICtrl.cs");

            foreach (var forbidden in new[]
                     {
                         "ConfigureResultArea", "CategorySelectionHandler", "EntrySelectionHandler",
                         "Instantiate(", "new GameObject", "SetParent(", "AddComponent<", "SetRect("
                     })
            {
                Assert.That(source, Does.Not.Contain(forbidden), forbidden);
            }

            Assert.That(source, Does.Contain("OnCategoryRowClick"));
            Assert.That(source, Does.Contain("OnEntryRowClick"));
            Assert.That(source, Does.Contain("categoryRows"));
            Assert.That(source, Does.Contain("entryRows"));
            Assert.That(source, Does.Contain("OnClickEvent(OnCategoryRowClick, row)"));
            Assert.That(source, Does.Contain("OnClickEvent(OnEntryRowClick, row)"));
            Assert.That(source, Does.Contain("RemoveAllOnClickEvents"));
        }

        [Test]
        public void PlayerMessages_UseLocalizedTextsInsteadOfTechnicalErrors()
        {
            var violations = Scan(HardcodedPlayerMessage);
            violations.AddRange(Scan(ForwardedTechnicalError));
            Assert.That(violations, Is.Empty, string.Join("\n", violations));
        }

        [Test]
        public void MessageButtons_DoNotUseDisplayTextAsBusinessData()
        {
            var notify = File.ReadAllText("Assets/Game/Scripts/Controller/Base/Notify.cs");
            var message = File.ReadAllText("Assets/Game/Scripts/Controller/UICtrl/Notify/MessageUICtrl.cs");
            var setting = File.ReadAllText("Assets/Game/Scripts/Controller/UICtrl/Home/SettingUICtrl.cs");
            var settingGeneral = File.ReadAllText("Assets/Game/Scripts/Controller/UICtrl/Home/Page/SettingGeneralPageCtrl.cs");
            var combined = notify + "\n" + message + "\n" + setting + "\n" + settingGeneral;

            Assert.That(combined, Does.Not.Contain("Action<" + "string> OnClick"));
            Assert.That(combined, Does.Not.Contain("OnClick?.Invoke(config." + "ButtonText)"));
            Assert.That(combined, Does.Not.Contain("string.Equals(config." + "ButtonText, Texts.CommonCancel"));
            Assert.That(combined, Does.Not.Contain("OnWindowModeSelected(string " + "label)"));
            Assert.That(combined, Does.Contain("MessageButtonRole"));
            Assert.That(combined, Does.Contain("Role = MessageButtonRole.Confirm"));
            Assert.That(combined, Does.Contain("Role = MessageButtonRole.Cancel"));
            Assert.That(settingGeneral, Does.Contain("ApplyDisplay(string resolution, FullScreenMode mode)"));
        }

        [Test]
        public void OnInitArchitectureEventsAreOwnedByCurrentBaseUi()
        {
            var violations = new List<string>();
            foreach (var path in Directory.GetFiles(ControllerRoot, "*.cs", SearchOption.AllDirectories)
                         .Where(path => !path.Replace('\\', '/').Contains("/Base/")))
            {
                var source = File.ReadAllText(path);
                foreach (var method in EnumerateNamedMethods(source, "OnInit"))
                {
                    foreach (var statement in method.MaskedBody.Split(';'))
                    {
                        if (!statement.Contains("RegisterEvent<"))
                        {
                            continue;
                        }

                        if (statement.Contains("=>") ||
                            !statement.Contains(".UnRegisterWhenGameObjectDestroyed(BaseUI)"))
                        {
                            violations.Add(FormatViolation(source, path, method.BodyStart + method.MaskedBody.IndexOf(statement, StringComparison.Ordinal)));
                        }
                    }
                }
            }

            Assert.That(violations, Is.Empty, string.Join("\n", violations));
        }

        [Test]
        public void BaseUiHasNoUnusedUpdateLoop()
        {
            var source = File.ReadAllText("Assets/Game/Scripts/Controller/Base/BaseUI.cs");
            Assert.That(source, Does.Not.Contain("OnTriggerUpdateEvent"));
            Assert.That(source, Does.Not.Contain("private void Update("));
            Assert.That(source, Does.Contain("OnTriggerDestroyEvent"));
            Assert.That(source, Does.Contain("private void OnDestroy("));
        }

        [Test]
        public void UnloadOverridesDoNotReferenceViewState()
        {
            var expected = new[]
            {
                "Assets/Game/Scripts/Controller/UICtrl/Inventory/BagUICtrl.cs",
                "Assets/Game/Scripts/Controller/UICtrl/Inventory/WarehouseUICtrl.cs",
                "Assets/Game/Scripts/Controller/UICtrl/Inventory/SearchContainerUICtrl.cs",
                "Assets/Game/Scripts/Controller/UICtrl/Expedition/ExpeditionHudUICtrl.cs",
                "Assets/Game/Scripts/Controller/UICtrl/Diagnostics/TestCmdUICtrl.cs",
                "Assets/Game/Scripts/Controller/UICtrl/Home/SettingUICtrl.cs",
                "Assets/Game/Scripts/Controller/UICtrl/Notify/MessageUICtrl.cs",
                "Assets/Game/Scripts/Controller/UICtrl/Notify/FloatUICtrl.cs"
            };
            var violations = new List<string>();
            foreach (var path in expected)
            {
                var source = File.ReadAllText(path);
                var methods = EnumerateNamedMethods(source, "OnUnload").ToArray();
                Assert.That(methods.Length, Is.EqualTo(1), path);
                var body = methods[0].MaskedBody;
                foreach (var token in new[] { "BaseUI", "UI.", "Transform", "transform", "ReturnTip", "ClearCallbacks", "EventSystem" })
                {
                    if (body.Contains(token))
                    {
                        violations.Add($"{path}: OnUnload contains {token}");
                    }
                }

            }

            Assert.That(violations, Is.Empty, string.Join("\n", violations));
        }

        [TestCase("UI.BtnResume?.onClick.AddListener(OnResumeClick);")]
        [TestCase("if (UI.BtnResume == null) return;")]
        [TestCase("row.BtnLoad?.onClick.RemoveAllListeners();")]
        [TestCase("if (row.BtnSelect != null) row.BtnSelect.interactable = enabled;")]
        [TestCase("if (slotRow.AmountText == null) return;")]
        [TestCase("if (null == entry.BtnDelete) return;")]
        public void RequiredBindingRule_RejectsInvalidDirectFieldSamples(string source)
        {
            Assert.That(
                RequiredBindingNullConditional.IsMatch(source) ||
                RequiredBindingNullComparison.IsMatch(source),
                Is.True);
        }

        [TestCase("private static bool IsOn(Toggle toggle) { return toggle == null || toggle.isOn; }")]
        [TestCase("private static void SetText(UnityEngine.UI.Button button) { if (button != null) Use(button); }")]
        [TestCase("private static void Bind(Toggle toggle) { toggle?.onValueChanged.AddListener(OnChanged); }")]
        public void RequiredBindingRule_RejectsInvalidParameterSamples(string source)
        {
            Assert.That(FindRequiredUiParameterNullGuards(source, "Sample.cs"), Is.Not.Empty);
        }

        [TestCase("private void Bind() { var closeButton = UI.BtnClose; if (closeButton == null) return; }")]
        [TestCase("private void Bind() { Button loadButton = row.BtnLoad; loadButton?.onClick.RemoveAllListeners(); }")]
        public void RequiredBindingRule_RejectsInvalidAliasSamples(string source)
        {
            Assert.That(FindRequiredUiAliasNullGuards(source, "Sample.cs"), Is.Not.Empty);
        }

        [TestCase("private static bool IsOn(Toggle toggle) { return toggle.isOn; }")]
        [TestCase("private static void SetText(Button button) { var label = button.GetComponentInChildren<TMP_Text>(true); if (label != null) label.text = \"x\"; }")]
        [TestCase("private void Enter(BaseSaveData data) { if (data == null) return; }")]
        [TestCase("private void Refresh() { if (UI == null) return; }")]
        public void RequiredBindingRule_AllowsValidSourceSamples(string source)
        {
            Assert.That(FindRequiredUiParameterNullGuards(source, "Sample.cs"), Is.Empty);
            Assert.That(FindRequiredUiAliasNullGuards(source, "Sample.cs"), Is.Empty);
            Assert.That(RequiredBindingNullConditional.IsMatch(source), Is.False);
            Assert.That(RequiredBindingNullComparison.IsMatch(source), Is.False);
        }

        [TestCase("ShowError(\"missing save data\");")]
        [TestCase("Notify.ShowFloatTips(result.Error);")]
        [TestCase("Notify.ShowFloatTips(error);")]
        [TestCase("Notify.ShowMessage(Texts.CommonConfirm, snapshot.Error);")]
        public void PlayerMessageRule_RejectsInvalidSamples(string source)
        {
            Assert.That(HardcodedPlayerMessage.IsMatch(source) || ForwardedTechnicalError.IsMatch(source), Is.True);
        }

        [TestCase("UI.BtnResume.onClick.AddListener(() => Hide());")]
        [TestCase("this.RegisterEvent<LanguageChangedEvent>(\n    _ => Refresh());")]
        public void EventRule_RejectsInlineLambdaSamples(string source)
        {
            Assert.That(InlineEventRegistration.IsMatch(source), Is.True);
        }

        [TestCase("UI.BtnResume.onClick.AddListener(OnResumeClick);")]
        [TestCase("this.RegisterEvent<LanguageChangedEvent>(OnLanguageChanged);")]
        [TestCase("config?.Buttons")]
        [TestCase("config.OnClick?.Invoke(null)")]
        public void Rules_AllowValidSamples(string source)
        {
            Assert.That(RequiredBindingNullConditional.IsMatch(source), Is.False);
            Assert.That(RequiredBindingNullComparison.IsMatch(source), Is.False);
            Assert.That(FindRequiredUiParameterNullGuards(source, "Sample.cs"), Is.Empty);
            Assert.That(FindRequiredUiAliasNullGuards(source, "Sample.cs"), Is.Empty);
            Assert.That(InlineEventRegistration.IsMatch(source), Is.False);
            Assert.That(HardcodedPlayerMessage.IsMatch(source), Is.False);
            Assert.That(ForwardedTechnicalError.IsMatch(source), Is.False);
        }

        private static List<string> FindRequiredUiParameterNullGuards(string source, string path)
        {
            var violations = new List<string>();
            foreach (var method in EnumerateMethods(source))
            {
                foreach (var parameter in ParseParameters(method.Parameters))
                {
                    if (!RequiredBindingTypeNames.Contains(parameter.TypeName))
                    {
                        continue;
                    }

                    violations.AddRange(FindVariableNullGuards(
                        source,
                        method.MaskedBody,
                        method.BodyStart,
                        path,
                        parameter.Name));
                }
            }

            return violations;
        }

        private static List<string> FindRequiredUiAliasNullGuards(string source, string path)
        {
            var violations = new List<string>();
            foreach (var method in EnumerateMethods(source))
            {
                foreach (Match match in AliasAssignment.Matches(method.MaskedBody))
                {
                    var alias = match.Groups["alias"].Value;
                    var bodyAfterAssignment = method.MaskedBody.Substring(match.Index + match.Length);
                    violations.AddRange(FindVariableNullGuards(
                        source,
                        bodyAfterAssignment,
                        method.BodyStart + match.Index + match.Length,
                        path,
                        alias));
                }
            }

            return violations;
        }

        private static IEnumerable<MethodSlice> EnumerateMethods(string source)
        {
            var maskedSource = MaskNonCodePreserveLines(source);
            foreach (Match match in MethodDeclaration.Matches(maskedSource))
            {
                var openBrace = maskedSource.IndexOf('{', match.Index + match.Length - 1);
                if (openBrace < 0)
                {
                    continue;
                }

                var closeBrace = FindMatchingBrace(maskedSource, openBrace);
                if (closeBrace <= openBrace)
                {
                    continue;
                }

                var bodyStart = openBrace + 1;
                yield return new MethodSlice(
                    match.Groups["parameters"].Value,
                    bodyStart,
                    maskedSource.Substring(bodyStart, closeBrace - bodyStart));
            }
        }

        private static IEnumerable<MethodSlice> EnumerateNamedMethods(string source, string methodName)
        {
            var maskedSource = MaskNonCodePreserveLines(source);
            var declaration = new Regex(
                $@"\b(?:public|private|protected|internal)\s+(?:(?:static|virtual|override|async|sealed|new|partial)\s+)*(?:[\w:<>,\.\[\]\?]+\s+)+{Regex.Escape(methodName)}\s*\((?<parameters>[^)]*)\)\s*\{{",
                RegexOptions.Compiled);
            foreach (Match match in declaration.Matches(maskedSource))
            {
                var openBrace = maskedSource.IndexOf('{', match.Index + match.Length - 1);
                if (openBrace < 0)
                {
                    continue;
                }

                var closeBrace = FindMatchingBrace(maskedSource, openBrace);
                if (closeBrace <= openBrace)
                {
                    continue;
                }

                var bodyStart = openBrace + 1;
                yield return new MethodSlice(
                    match.Groups["parameters"].Value,
                    bodyStart,
                    maskedSource.Substring(bodyStart, closeBrace - bodyStart));
            }
        }

        private static IEnumerable<ParameterInfoSlice> ParseParameters(string parameters)
        {
            foreach (var rawParameter in parameters.Split(','))
            {
                var parameter = rawParameter.Trim();
                if (parameter.Length == 0)
                {
                    continue;
                }

                parameter = Regex.Replace(parameter, @"^\[[^\]]+\]\s*", string.Empty);
                parameter = Regex.Replace(parameter, @"\s*=\s*.+$", string.Empty);
                parameter = Regex.Replace(parameter, @"\b(?:in|out|ref|params)\s+", string.Empty);

                var match = Regex.Match(parameter, @"(?<type>.+?)\s+(?<name>[A-Za-z_]\w*)$");
                if (!match.Success)
                {
                    continue;
                }

                var typeName = match.Groups["type"].Value
                    .Trim()
                    .Replace("global::", string.Empty);
                yield return new ParameterInfoSlice(typeName, match.Groups["name"].Value);
            }
        }

        private static IEnumerable<string> FindVariableNullGuards(
            string source,
            string maskedRegion,
            int regionStart,
            string path,
            string variableName)
        {
            var escaped = Regex.Escape(variableName);
            var nullConditional = new Regex($@"\b{escaped}\s*\?\.");
            var nullComparison = new Regex(
                $@"(?:\b{escaped}\s*(?:==|!=)\s*null|\bnull\s*(?:==|!=)\s*{escaped}\b)");

            foreach (Match match in nullConditional.Matches(maskedRegion))
            {
                yield return FormatViolation(source, path, regionStart + match.Index);
            }

            foreach (Match match in nullComparison.Matches(maskedRegion))
            {
                yield return FormatViolation(source, path, regionStart + match.Index);
            }
        }

        private static int FindMatchingBrace(string maskedSource, int openBrace)
        {
            var depth = 0;
            for (var index = openBrace; index < maskedSource.Length; index++)
            {
                if (maskedSource[index] == '{')
                {
                    depth++;
                }
                else if (maskedSource[index] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return index;
                    }
                }
            }

            return -1;
        }

        private static string MaskNonCodePreserveLines(string source)
        {
            var chars = source.ToCharArray();
            for (var index = 0; index < chars.Length;)
            {
                if (chars[index] == '/' && index + 1 < chars.Length && chars[index + 1] == '/')
                {
                    index = MaskUntilNewLine(chars, index);
                }
                else if (chars[index] == '/' && index + 1 < chars.Length && chars[index + 1] == '*')
                {
                    index = MaskBlockComment(chars, index);
                }
                else if (chars[index] == '@' && index + 1 < chars.Length && chars[index + 1] == '"')
                {
                    index = MaskVerbatimString(chars, index);
                }
                else if (chars[index] == '"')
                {
                    index = MaskString(chars, index);
                }
                else if (chars[index] == '\'')
                {
                    index = MaskCharLiteral(chars, index);
                }
                else
                {
                    index++;
                }
            }

            return new string(chars);
        }

        private static int MaskUntilNewLine(char[] chars, int index)
        {
            while (index < chars.Length && chars[index] != '\n')
            {
                chars[index++] = ' ';
            }

            return index;
        }

        private static int MaskBlockComment(char[] chars, int index)
        {
            chars[index++] = ' ';
            chars[index++] = ' ';
            while (index < chars.Length)
            {
                if (chars[index] == '*' && index + 1 < chars.Length && chars[index + 1] == '/')
                {
                    chars[index++] = ' ';
                    chars[index++] = ' ';
                    break;
                }

                if (chars[index] != '\n')
                {
                    chars[index] = ' ';
                }

                index++;
            }

            return index;
        }

        private static int MaskString(char[] chars, int index)
        {
            chars[index++] = ' ';
            while (index < chars.Length)
            {
                if (chars[index] == '\\')
                {
                    chars[index++] = ' ';
                    if (index < chars.Length && chars[index] != '\n')
                    {
                        chars[index++] = ' ';
                    }
                    continue;
                }

                var current = chars[index];
                chars[index++] = current == '\n' ? '\n' : ' ';
                if (current == '"')
                {
                    break;
                }
            }

            return index;
        }

        private static int MaskVerbatimString(char[] chars, int index)
        {
            chars[index++] = ' ';
            chars[index++] = ' ';
            while (index < chars.Length)
            {
                var current = chars[index];
                chars[index++] = current == '\n' ? '\n' : ' ';
                if (current == '"')
                {
                    if (index < chars.Length && chars[index] == '"')
                    {
                        chars[index++] = ' ';
                        continue;
                    }

                    break;
                }
            }

            return index;
        }

        private static int MaskCharLiteral(char[] chars, int index)
        {
            chars[index++] = ' ';
            while (index < chars.Length)
            {
                if (chars[index] == '\\')
                {
                    chars[index++] = ' ';
                    if (index < chars.Length && chars[index] != '\n')
                    {
                        chars[index++] = ' ';
                    }
                    continue;
                }

                var current = chars[index];
                chars[index++] = current == '\n' ? '\n' : ' ';
                if (current == '\'')
                {
                    break;
                }
            }

            return index;
        }

        private static List<string> Scan(Regex regex)
        {
            var violations = new List<string>();
            foreach (var path in Directory.GetFiles(ControllerRoot, "*.cs", SearchOption.AllDirectories))
            {
                var source = File.ReadAllText(path);
                foreach (Match match in regex.Matches(source))
                {
                    violations.Add(FormatViolation(source, path, match.Index));
                }
            }

            return violations;
        }

        private static string FormatViolation(string source, string path, int index)
        {
            var line = 1;
            for (var cursor = 0; cursor < index && cursor < source.Length; cursor++)
            {
                if (source[cursor] == '\n') line++;
            }

            var lines = source.Split('\n');
            var text = line <= lines.Length ? lines[line - 1].Trim() : string.Empty;
            return $"{path}:{line}: {text}";
        }

        private readonly struct MethodSlice
        {
            public MethodSlice(string parameters, int bodyStart, string maskedBody)
            {
                Parameters = parameters;
                BodyStart = bodyStart;
                MaskedBody = maskedBody;
            }

            public string Parameters { get; }
            public int BodyStart { get; }
            public string MaskedBody { get; }
        }

        private readonly struct ParameterInfoSlice
        {
            public ParameterInfoSlice(string typeName, string name)
            {
                TypeName = typeName;
                Name = name;
            }

            public string TypeName { get; }
            public string Name { get; }
        }
    }
}
