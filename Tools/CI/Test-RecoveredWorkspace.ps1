[CmdletBinding()]
param([string]$ReportPath)
$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetTempPath()) ('FirepowerRetry-' + [Guid]::NewGuid().ToString('N'))
$results = New-Object 'System.Collections.Generic.List[object]'
$guard = Join-Path $PSScriptRoot 'Assert-FirepowerWorkspace.ps1'
function Check([string]$name, [bool]$accept) {
    $ok = $true
    try { & $guard -WorkspaceRoot $root -ExpectedCommit $commit | Out-Null } catch { $ok = $false }
    if ($ok -ne $accept) { throw "Unexpected workspace ruling: $name; accepted=$ok" }
    $results.Add(@{Name=$name;Passed=$true})
}
try {
    $client = Join-Path $root 'Client'
    $ledgerPath = Join-Path $client 'Assets/Editor/Build/windows-release-version.json'
    [IO.Directory]::CreateDirectory((Split-Path $ledgerPath -Parent)) | Out-Null
    $ledger = [ordered]@{schemaVersion=1;productId='com.firepowerloop.game';platform='Windows';architecture='x64';versionName='0.0.1';releaseNumber=2}
    [IO.File]::WriteAllText($ledgerPath, ($ledger | ConvertTo-Json))
    [IO.File]::WriteAllText((Join-Path $root '.gitignore'), '/Release/')
    & git -C $root init --quiet
    & git -C $root add .
    & git -C $root -c user.name=CiFileTest -c user.email=ci-file-test@example.invalid commit -m fixture --quiet
    if ($LASTEXITCODE -ne 0) { throw 'fixture git failed' }
    $commit = (& git -C $root rev-parse HEAD).Trim()
    Check 'CleanCheckoutAccepted' $true
    $ledger.releaseNumber=3; [IO.File]::WriteAllText($ledgerPath, ($ledger | ConvertTo-Json))
    Check 'UnprovenLedgerRejected' $false
    $sha=[Security.Cryptography.SHA256]::Create()
    try { $key=([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($client.ToLowerInvariant())))).Replace('-','').ToLowerInvariant().Substring(0,16) } finally { $sha.Dispose() }
    $state=Join-Path $root ('Release/CiWorkspaceRecovery/'+$key)
    [IO.Directory]::CreateDirectory($state) | Out-Null
    $proof=@{SchemaVersion=1;ProjectId='FirepowerLoop';Client=$client;Commit=$commit;RequestId='interrupted';LedgerPath='Client/Assets/Editor/Build/windows-release-version.json';LedgerHash=(Get-FileHash -LiteralPath $ledgerPath -Algorithm SHA256).Hash.ToLowerInvariant()}
    $proofFile=Join-Path $state 'retry.json'
    function Save-Proof { [IO.File]::WriteAllText($proofFile, ($proof | ConvertTo-Json)) }
    Save-Proof; Check 'RecoveredMonotonicLedgerAccepted' $true
    $proof.Commit='0'*40; Save-Proof; Check 'OtherCommitRejected' $false
    $proof.Commit=$commit; $proof.Client=Join-Path $root 'Other'; Save-Proof; Check 'OtherWorkspaceRejected' $false
    $proof.Client=$client; Save-Proof
    $ledger.releaseNumber=4; [IO.File]::WriteAllText($ledgerPath, ($ledger | ConvertTo-Json)); Check 'ChangedAfterRecoveryRejected' $false
    $ledger.releaseNumber=1; [IO.File]::WriteAllText($ledgerPath, ($ledger | ConvertTo-Json)); $proof.LedgerHash=(Get-FileHash -LiteralPath $ledgerPath -Algorithm SHA256).Hash.ToLowerInvariant(); Save-Proof; Check 'LowerSequenceRejected' $false
    $ledger.releaseNumber=3; $ledger.productId='other'; [IO.File]::WriteAllText($ledgerPath, ($ledger | ConvertTo-Json)); $proof.LedgerHash=(Get-FileHash -LiteralPath $ledgerPath -Algorithm SHA256).Hash.ToLowerInvariant(); Save-Proof; Check 'ChangedIdentityRejected' $false
    $ledger.productId='com.firepowerloop.game'; [IO.File]::WriteAllText($ledgerPath, ($ledger | ConvertTo-Json)); $proof.LedgerHash=(Get-FileHash -LiteralPath $ledgerPath -Algorithm SHA256).Hash.ToLowerInvariant(); Save-Proof
    & git -C $root add -- Client/Assets/Editor/Build/windows-release-version.json
    Check 'StagedLedgerRejected' $false
    & git -C $root restore --staged -- Client/Assets/Editor/Build/windows-release-version.json
    [IO.File]::WriteAllText((Join-Path $root 'unrelated.txt'), 'user'); Check 'OtherDirtyFileRejected' $false
    [IO.File]::Delete((Join-Path $root 'unrelated.txt'))
    [IO.File]::WriteAllText((Join-Path $state 'active.json'), '{}'); Check 'UnrecoveredAttemptRejected' $false
    $json=@{Scope='RecoveredWorkspaceOnly';Passed=$results.Count;Failed=0;UnityLaunched=$false;PlayerBuild=$false;Results=$results} | ConvertTo-Json -Depth 6
    if ($ReportPath) { [IO.File]::WriteAllText([IO.Path]::GetFullPath($ReportPath),$json) }
    $json
} finally {
    $resolved=[IO.Path]::GetFullPath($root)
    $allowed=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\FirepowerRetry-'
    if (-not $resolved.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)) { throw 'Cleanup boundary rejected' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
