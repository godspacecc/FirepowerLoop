"""读取源码检查公告编辑器边界，不调用 Unity 或保存公告。"""
from pathlib import Path
import json
import subprocess

root = Path(__file__).resolve().parents[3]
utility_path = "Client/Assets/Editor/YooAsset/ResourceUpdateNoticeEditorUtility.cs"
window_path = "Client/Assets/Editor/YooAsset/ResourceUpdateNoticeEditorWindow.cs"
utility = (root / utility_path).read_text(encoding="utf-8-sig")
window = (root / window_path).read_text(encoding="utf-8-sig")
baseline = subprocess.check_output(["git", "show", "88efab37:" + utility_path], cwd=root).decode("utf-8-sig").replace("\r\n", "\n")

def method(text, signature):
    start = text.index(signature)
    end = text.index("\n        }", start) + len("\n        }")
    return text[start:end]

assert "EditorUserBuildSettings.activeBuildTarget" in utility
assert 'Path.Combine(releaseRoot, "Android")' in utility
assert "BuildTarget.StandaloneWindows64" in utility
target = method(utility, "internal static bool IsSameTarget(")
for field in ["Platform", "Version", "BundleVersion", "LatestBundle"]:
    assert "edited." + field in target and "current." + field in target
load = method(utility, "internal static NoticeEditState LoadEditState(")
assert "File.Exists(noticeFilePath)" in load
assert "Write" not in load and "EnsureNoticeFile" not in utility
assert "AssetDatabase.Refresh" not in utility
assert method(utility, "private static void SafeReplace(") == method(baseline, "private static void SafeReplace(")
save_detailed = method(utility, "internal static NoticeSaveResult SaveDetailed(")
assert save_detailed.index("SafeReplace(noticeFilePath, json)") < save_detailed.index("SafeReplace(target, json)")
assert "DirectoryNotFoundException" in save_detailed
assert "发布目录同步失败，源文件未回退" in save_detailed
save = method(window, "private void Save()")
assert save.index("IsSameTarget") < save.index("SaveDetailed")
assert "state.Version =" not in save and "Reload();" not in save
assert "dirty = !result.Success" in save
reload = method(window, "private void Reload()")
assert reload.index("if (dirty)") < reload.index("return;") < reload.index("LoadCurrent(current)")
assert "DisplayDialog" in method(window, "private void RequestReload()")
assert "SerializeObject" in method(window, "private void CacheDraft()")
assert "DeserializeObject" in method(window, "private void RestoreDraft()")
assert window.count("GUI.changed = true;") == 2
result = dict(Baseline="88efab37", StaticChecks="passed", SafeReplaceUnchanged=True,
              UnityOperated=False, TestsRun=False, NoticeWritten=False,
              RuntimeDraftRestoreVerified=False)
(Path(__file__).parent / "static-check.json").write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print("PASS: platform routing, target guard, dirty-state protection, save ordering, unchanged file replacement.")
