using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace Game
{
    public sealed class ResourceDiagnosticsWindow : Shared.UnityTools.ResourceInspectionWindow
    {
        [MenuItem(MenuPath, false, 300)]
        public static void OpenWindow() => Open<ResourceDiagnosticsWindow>();

        public static IReadOnlyList<EditorDiagnosticIssue> RunScan() => MissingScriptDiagnostics.ScanProject();

        protected override IEnumerable<(string Path, string Object, string Message)> ScanFindings() =>
            RunScan().Select(item => (item.AssetPath, item.HierarchyPath, item.Message));
    }
}
