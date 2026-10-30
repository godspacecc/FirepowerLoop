$ErrorActionPreference = 'Stop'
$path = Join-Path (Get-Location) 'Server/GameServer/Publish-win-x64.ps1'
$tokens = $null
$parseErrors = $null
# 仅构建语法树，不加载或执行生成脚本。
$ast = [System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$parseErrors)
$messages = @($parseErrors | ForEach-Object { $_.ToString() })
@{ PowerShellVersion=$PSVersionTable.PSVersion.ToString(); Script=$path; SyntaxErrors=$messages; ScriptExecuted=$false } | ConvertTo-Json -Depth 4 | Set-Content -Encoding UTF8 'Backup/Artifacts/OnlineServerPackage-20261030/powershell-syntax.json'
if ($messages.Count) { throw ($messages -join "`n") }
Write-Output 'Windows PowerShell syntax parsing passed; publish script was not executed.'
