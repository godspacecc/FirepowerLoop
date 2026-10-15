param([Parameter(Mandatory=$true)][string]$Source, [Parameter(Mandatory=$true)][string]$Destination, [Parameter(Mandatory=$true)][string]$Pattern)
$ErrorActionPreference = 'Stop'
function Get-GeneratedHash([string]$Path) {
    $generatedHash = [Security.Cryptography.SHA256]::Create()
    try { return [Convert]::ToBase64String($generatedHash.ComputeHash([IO.File]::ReadAllBytes($Path))) }
    finally { $generatedHash.Dispose() }
}
# 跳过相同产物，避免 Unity/IDE 映射中的未变化文件被 COPY 截断；仍由唯一 gen.bat 调用。
foreach ($generatedFile in (Get-ChildItem -LiteralPath $Source -Filter $Pattern -File)) {
    $generatedTarget = Join-Path $Destination $generatedFile.Name
    if ((Test-Path -LiteralPath $generatedTarget) -and
        (Get-GeneratedHash $generatedFile.FullName) -eq (Get-GeneratedHash $generatedTarget)) { continue }
    Copy-Item -LiteralPath $generatedFile.FullName -Destination $generatedTarget -Force
}
