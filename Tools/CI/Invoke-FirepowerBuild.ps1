[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidateSet('PC','Android')][string]$Platform,
    [Parameter(Mandatory=$true)][ValidateSet('FirstPackage','HostUpdate')][string]$BuildType,
    [Parameter(Mandatory=$true)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$ExpectedCommit,
    [string]$WorkspaceRoot = (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent),
    [string]$UnityExe = $env:FIREPOWER_UNITY_EXE,
    [string]$InstallerCompiler = $env:FIREPOWER_ISCC,
    [string]$BaselineDirectory,
    [string]$PublishDirectory,
    [switch]$Publish,
    [switch]$Build,
    [switch]$Execute
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# 默认为只生成请求；-Execute才启动独立Unity，-Build才允许请求真实构建。
$repo = [IO.Path]::GetFullPath($WorkspaceRoot)
$client = Join-Path $repo 'Client'
if (-not (Test-Path -LiteralPath (Join-Path $client 'ProjectSettings/ProjectVersion.txt') -PathType Leaf)) { throw 'Not a FirepowerLoop Client workspace.' }
$version = Get-Content -LiteralPath (Join-Path $client 'ProjectSettings/ProjectVersion.txt') -Raw
if ($version -notmatch '2022\.3\.55f1c1') { throw 'Unity version must match this project: 2022.3.55f1c1.' }
$actual = & git -C $repo rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $actual.Trim() -ine $ExpectedCommit) { throw 'Workspace commit does not match ExpectedCommit.' }
if ($BuildType -eq 'HostUpdate' -and (-not [IO.Path]::IsPathRooted($BaselineDirectory) -or -not (Test-Path -LiteralPath (Join-Path $BaselineDirectory 'baseline.json') -PathType Leaf))) { throw 'HostUpdate requires an explicit archived baseline.' }
if ($Publish -and (-not [IO.Path]::IsPathRooted($PublishDirectory) -or (Test-Path -LiteralPath $PublishDirectory))) { throw 'Publishing requires a new absolute destination.' }

$id = 'ci-' + [Guid]::NewGuid().ToString('N')
$output = Join-Path $repo ('Release/CiRequests/' + $id)
[IO.Directory]::CreateDirectory($output) | Out-Null
$requestPath = Join-Path $output 'request.json'
$reportPath = Join-Path $output 'report.json'
$logPath = Join-Path $output 'unity.log'
$request = [ordered]@{ SchemaVersion=1; ProjectId='FirepowerLoop'; RequestId=$id; ProjectPath=$client; Platform=$Platform; BuildType=$BuildType; ExpectedCommit=$ExpectedCommit; ValidateOnly=(-not $Build); Publish=[bool]$Publish }
if ($BuildType -eq 'HostUpdate') { $request.BaselineDirectory = [IO.Path]::GetFullPath($BaselineDirectory) }
if ($Publish) { $request.PublishDirectory = [IO.Path]::GetFullPath($PublishDirectory) }
if ($InstallerCompiler) { $request.InstallerCompiler = [IO.Path]::GetFullPath($InstallerCompiler) }
$request.CancellationFile = Join-Path $output 'cancel.request'
[IO.File]::WriteAllText($requestPath, ($request | ConvertTo-Json -Depth 6), (New-Object Text.UTF8Encoding($false)))
Write-Output "Request: $requestPath"
Write-Output "Graceful cancellation signal: $($request.CancellationFile)"
if (-not $Execute) { Write-Output "Prepared only: $requestPath"; return }

# 独立构建节点必须是干净的固定提交；不清理、重置或杀死任何开发者进程。
& (Join-Path $PSScriptRoot 'Assert-FirepowerWorkspace.ps1') -WorkspaceRoot $repo -ExpectedCommit $ExpectedCommit
if (Test-Path -LiteralPath (Join-Path $client 'Temp/UnityLockfile')) { throw 'Unity is already using this Client; choose a dedicated checkout.' }
if (-not [IO.Path]::IsPathRooted($UnityExe) -or -not (Test-Path -LiteralPath $UnityExe -PathType Leaf)) { throw 'Set FIREPOWER_UNITY_EXE to the project Unity.exe.' }
if ($Build -and $Platform -eq 'PC' -and $BuildType -eq 'FirstPackage' -and (-not $InstallerCompiler -or -not (Test-Path -LiteralPath $InstallerCompiler -PathType Leaf))) { throw 'PC first package requires explicit FIREPOWER_ISCC.' }
$target = if ($Platform -eq 'PC') { 'Win64' } else { 'Android' }
$unityArguments = @('-batchmode','-nographics','-projectPath',$client,'-buildTarget',$target,'-executeMethod','Game.Editor.CiBuildEntry.Execute','-firepowerRequest',$requestPath,'-firepowerReport',$reportPath,'-logFile',$logPath)
& $UnityExe @unityArguments
$unityExitCode = $LASTEXITCODE
if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) { throw "Unity returned $unityExitCode without a report. Log: $logPath" }
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
if ($report.Status -eq 'Cancelled' -and $report.ExitCode -eq 2) { Write-Output "CI cancelled at $($report.Checkpoint). RecoveryRequired=$($report.RecoveryRequired). Report: $reportPath"; exit 2 }
if ($unityExitCode -ne 0 -or $report.ExitCode -ne 0) { throw "CI failed at $($report.Stage): $($report.Error). Report: $reportPath" }
Write-Output "CI $($report.Status): $reportPath"
