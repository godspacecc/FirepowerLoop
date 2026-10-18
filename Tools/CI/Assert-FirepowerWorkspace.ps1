[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$WorkspaceRoot, [Parameter(Mandatory=$true)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$ExpectedCommit)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
# 唯一允许的源差异：同Commit显式恢复留下的、内容哈希匹配且序号不降低的Windows台账。
$repo=[IO.Path]::GetFullPath($WorkspaceRoot).TrimEnd('\','/')
$client=Join-Path $repo 'Client'
$actual=(& git -C $repo rev-parse HEAD)
if ($LASTEXITCODE -ne 0 -or $actual.Trim() -ine $ExpectedCommit) { throw 'Workspace commit differs from ExpectedCommit.' }
$sha=[Security.Cryptography.SHA256]::Create()
try { $key=([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($client.ToLowerInvariant())))).Replace('-','').ToLowerInvariant().Substring(0,16) } finally { $sha.Dispose() }
$state=Join-Path $repo ('Release/CiWorkspaceRecovery/'+$key)
function Assert-NoLinks([string]$path) {
    for ($cursor=[IO.Path]::GetFullPath($path); $cursor; $cursor=[IO.Path]::GetDirectoryName($cursor)) {
        if ((Test-Path -LiteralPath $cursor) -and ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'CI recovery path cannot contain links.' }
    }
}
Assert-NoLinks $state
if (Test-Path -LiteralPath (Join-Path $state 'active.json')) { throw 'CI workspace requires explicit recovery first.' }
$dirty=@(& git -C $repo -c core.quotepath=false status --porcelain --untracked-files=all)
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect CI checkout.' }
if ($dirty.Count -eq 0) { return }
$relative='Client/Assets/Editor/Build/windows-release-version.json'
if ($dirty.Count -ne 1 -or $dirty[0] -cne (' M '+$relative)) { throw 'CI checkout has unapproved source changes.' }
$proofFile=Join-Path $state 'retry.json'
$ledgerFile=Join-Path $repo $relative
Assert-NoLinks $proofFile; Assert-NoLinks $ledgerFile
$proof=Get-Content -LiteralPath $proofFile -Raw | ConvertFrom-Json
if ($proof.SchemaVersion -ne 1 -or $proof.ProjectId -cne 'FirepowerLoop' -or $proof.Client -ine $client -or $proof.Commit -ine $ExpectedCommit -or $proof.LedgerPath -cne $relative -or -not $proof.RequestId -or $proof.LedgerHash -ine (Get-FileHash -LiteralPath $ledgerFile -Algorithm SHA256).Hash) { throw 'Recovered ledger proof does not match workspace, commit or content.' }
$baseText=& git -C $repo show ($ExpectedCommit+':'+$relative)
if ($LASTEXITCODE -ne 0) { throw 'Missing tracked version ledger baseline.' }
$baseline=($baseText -join "`n") | ConvertFrom-Json
$current=Get-Content -LiteralPath $ledgerFile -Raw | ConvertFrom-Json
$baseKeys=@($baseline.PSObject.Properties.Name | Sort-Object)
$currentKeys=@($current.PSObject.Properties.Name | Sort-Object)
if (($baseKeys -join '|') -cne ($currentKeys -join '|')) { throw 'Version ledger shape changed.' }
foreach ($name in $baseKeys) {
    if ($name -ne 'releaseNumber' -and ($baseline.$name | ConvertTo-Json -Compress) -cne ($current.$name | ConvertTo-Json -Compress)) { throw "Version ledger identity changed: $name" }
}
if ($current.schemaVersion -ne 1 -or $current.productId -cne 'com.firepowerloop.game' -or $current.platform -cne 'Windows' -or $current.architecture -cne 'x64' -or $current.versionName -notmatch '^\d+\.\d+\.\d+$' -or $current.releaseNumber -isnot [long] -and $current.releaseNumber -isnot [int] -or $current.releaseNumber -lt $baseline.releaseNumber -or $current.releaseNumber -ge [int]::MaxValue) { throw 'Recovered version ledger identity or monotonic sequence is invalid.' }
