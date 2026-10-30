"""只读取源码核对生成顺序与修改范围，不执行服务端或生成脚本。"""
from pathlib import Path
import json
import re
import subprocess

root = Path(__file__).resolve().parents[3]
path = "Server/GameServer/Publish-win-x64.ps1"
source = (root / path).read_text(encoding="utf-8-sig")
baseline = subprocess.check_output(
    ["git", "show", "6c3b8920:" + path], cwd=root
).decode("utf-8-sig").replace("\r\n", "\n")

def function(text, name):
    start = text.index("function " + name + " {")
    # 下一函数之前可能有新注释，仅比较实际函数体至顶格结束括号。
    end = text.index("\n}", start) + 2
    return text[start:end]

unchanged = ["Resolve-AndValidateContext", "Invoke-WindowsPublish",
             "Copy-PublishConfig", "Copy-DeploymentLaunchers", "Validate-PublishOutput",
             "Acquire-PublishLock", "Release-PublishLock", "Assert-ChildPath"]
for name in unchanged:
    assert function(source, name) == function(baseline, name), name

main = source[source.index('\ntry {\n    Write-Host "正在准备'):]
steps = ["Acquire-PublishLock", "$pendingBackups.Count -gt 0",
         "Invoke-WindowsPublish -Context $script:stageContext",
         "Copy-PublishConfig -Context $script:stageContext",
         "Copy-DeploymentLaunchers -Context $script:stageContext",
         "Validate-PublishOutput -Context $script:stageContext",
         "Install-PublishOutput -Context $context", "生成成功"]
positions = [main.index(step) for step in steps]
assert positions == sorted(positions)
assert "Remove-PublishOutputs" not in source
assert "$script:managedNames = @('Bin', 'Config', 'Tools', '启动联机服务器.bat', '局域网部署说明.md')" in source
assert (root / path).read_bytes().startswith(b"\xef\xbb\xbf")
install = function(source, "Install-PublishOutput")
assert install.index("-cne $expectedNew") < install.index("Remove-TransactionDirectory")
assert install.index("-cne $expectedOld") < install.index("Remove-TransactionDirectory")
assert "无法确认旧包恢复" in install
assert "if (-not (Test-Path -LiteralPath $backupRoot))" in main
cleanup = function(source, "Remove-TransactionDirectory")
assert cleanup.index("Assert-ChildPath") < cleanup.index("Assert-PlainTree") < cleanup.index("Remove-Item")
assert len(re.findall(r"\bRemove-Item\b", source)) == 1
move = function(source, "Move-OutputEntry")
assert move.index("Assert-ChildPath") < move.index("Assert-PlainTree") < move.index("::Move")

menu = (root / "Client/Assets/Editor/GameServer/LocalOnlineServerPublishMenu.cs").read_text(encoding="utf-8-sig")
assert "Tools/游戏服务器/生成联机服务器包" in menu
assert "请在窗口查看最终结果" in menu
result = dict(Baseline="6c3b8920", UnchangedFunctions=unchanged,
              StaticSourceChecks="passed", PublishScriptExecuted=False,
              RuntimeRollbackVerified=False, UnityTestsRun=False)
(Path(__file__).parent / "static-check.json").write_text(
    json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print("PASS: source scope, ordering, managed paths, recovery preservation and UTF-8 BOM.")
