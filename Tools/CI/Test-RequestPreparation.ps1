[CmdletBinding()]
param([string]$ReportPath)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$scriptUnderTest = Join-Path $PSScriptRoot 'Invoke-FirepowerBuild.ps1'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('FirepowerCiRequest-' + [Guid]::NewGuid().ToString('N'))
$results = New-Object 'System.Collections.Generic.List[object]'
function Assert-Condition([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Expect-Rejected([string]$Name, [scriptblock]$Action) {
    $rejected = $false
    try { & $Action | Out-Null } catch { $rejected = $true }
    Assert-Condition $rejected $Name
    $results.Add([ordered]@{ Name=$Name; Passed=$true })
}
try {
    [IO.Directory]::CreateDirectory((Join-Path $testRoot 'Client/ProjectSettings')) | Out-Null
    [IO.File]::WriteAllText((Join-Path $testRoot 'Client/ProjectSettings/ProjectVersion.txt'), 'm_EditorVersion: 2022.3.55f1c1')
    & git -C $testRoot init --quiet
    if ($LASTEXITCODE -ne 0) { throw 'Cannot initialize temporary test repository.' }
    & git -C $testRoot -c user.name=CiFileTest -c user.email=ci-file-test@example.invalid commit --allow-empty -m fixture --quiet
    if ($LASTEXITCODE -ne 0) { throw 'Cannot create temporary test commit.' }
    $commit = (& git -C $testRoot rev-parse HEAD).Trim()
    & $scriptUnderTest -WorkspaceRoot $testRoot -Platform PC -BuildType FirstPackage -ExpectedCommit $commit -UnityExe 'Z:/not-called/Unity.exe' | Out-Null
    $requests = @(Get-ChildItem -LiteralPath (Join-Path $testRoot 'Release/CiRequests') -Filter request.json -Recurse)
    Assert-Condition ($requests.Count -eq 1) 'Default preparation should create one request.'
    $value = Get-Content -LiteralPath $requests[0].FullName -Raw | ConvertFrom-Json
    Assert-Condition ($value.ValidateOnly -and -not $value.Publish -and $value.ExpectedCommit -eq $commit) 'Default request must validate only and never publish.'
    $results.Add([ordered]@{ Name='DefaultPrepareDoesNotLaunchUnityOrPublish'; Passed=$true })
    & $scriptUnderTest -WorkspaceRoot $testRoot -Platform Android -BuildType FirstPackage -ExpectedCommit $commit -Build | Out-Null
    $requests = @(Get-ChildItem -LiteralPath (Join-Path $testRoot 'Release/CiRequests') -Filter request.json -Recurse)
    Assert-Condition ($requests.Count -eq 2) 'Explicit build preparation should retain the first request.'
    Assert-Condition (@($requests | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json } | Where-Object { $_.ValidateOnly -eq $false }).Count -eq 1) 'Build switch should only change the prepared request.'
    $results.Add([ordered]@{ Name='BuildFlagPreparesButDoesNotExecute'; Passed=$true })
    Expect-Rejected 'WrongCommitRejected' { & $scriptUnderTest -WorkspaceRoot $testRoot -Platform PC -BuildType FirstPackage -ExpectedCommit ('0' * 40) }
    Expect-Rejected 'HostNeedsExplicitArchive' { & $scriptUnderTest -WorkspaceRoot $testRoot -Platform Android -BuildType HostUpdate -ExpectedCommit $commit }
    Expect-Rejected 'PublishCannotOverwriteExistingDirectory' { & $scriptUnderTest -WorkspaceRoot $testRoot -Platform PC -BuildType FirstPackage -ExpectedCommit $commit -Publish -PublishDirectory $testRoot }
    $report = [ordered]@{ Scope='RequestPreparationOnly'; Passed=$results.Count; Failed=0; UnityLaunched=$false; PlayerBuild=$false; Results=$results }
    $json = $report | ConvertTo-Json -Depth 8
    if ($ReportPath) { [IO.File]::WriteAllText([IO.Path]::GetFullPath($ReportPath), $json, (New-Object Text.UTF8Encoding($false))) }
    Write-Output $json
}
finally {
    # 删除只限本次明确创建的系统临时目录，不删除任何用户checkout或发布包。
    $resolved = [IO.Path]::GetFullPath($testRoot)
    $allowed = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\FirepowerCiRequest-'
    if (-not $resolved.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Temporary cleanup boundary rejected.' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
