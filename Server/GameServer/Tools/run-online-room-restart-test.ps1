param(
    [string]$Address = "127.0.0.1:31011",
    [string]$UserName = "",
    [string]$StorePath = "",
    [int]$StartupTimeoutSeconds = 20
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$script:ServerProcess = $null
$script:ServerLogSinks = @()
$script:Failed = 0

[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$OutputEncoding = [System.Text.UTF8Encoding]::new($false)

function Write-Step([string]$name) {
    Write-Host ""
    Write-Host "===== $name ====="
}

function Stop-OnlineServer {
    if ($script:ServerProcess -and -not $script:ServerProcess.HasExited) {
        Stop-Process -Id $script:ServerProcess.Id -Force
        $script:ServerProcess.WaitForExit(5000) | Out-Null
    }
    $script:ServerProcess = $null
}

function Close-ServerLogSinks {
    foreach ($sink in $script:ServerLogSinks) {
        try {
            if (-not $sink.Process.HasExited) {
                $sink.Process.WaitForExit(5000) | Out-Null
            }
            [System.IO.File]::WriteAllText($sink.StdoutPath, $sink.StdoutTask.Result, [System.Text.UTF8Encoding]::new($false))
            [System.IO.File]::WriteAllText($sink.StderrPath, $sink.StderrTask.Result, [System.Text.UTF8Encoding]::new($false))
        } catch {
        }
    }
    $script:ServerLogSinks = @()
}

function Wait-Port([string]$hostName, [int]$port, [int]$timeoutSeconds) {
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $client = [System.Net.Sockets.TcpClient]::new()
            $task = $client.ConnectAsync($hostName, $port)
            if ($task.Wait(300) -and $client.Connected) {
                $client.Dispose()
                return
            }
            $client.Dispose()
        } catch {
        }
        Start-Sleep -Milliseconds 300
        if ($script:ServerProcess -and $script:ServerProcess.HasExited) {
            throw "Server exited before opening $hostName`:$port. ExitCode=$($script:ServerProcess.ExitCode)"
        }
    }
    throw "Timed out waiting for $hostName`:$port"
}

function Start-DotNetProcess([string[]]$arguments, [string]$stdout, [string]$stderr) {
    $dotnet = (Get-Command dotnet).Source
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $dotnet
    $psi.WorkingDirectory = $BinDir
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.Arguments = (($arguments | ForEach-Object {
        if ($_ -match '\s') { '"' + $_.Replace('"', '\"') + '"' } else { $_ }
    }) -join " ")
    New-Item -ItemType Directory -Force -Path ([System.IO.Path]::GetDirectoryName($stdout)) | Out-Null
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $psi
    $process.Start() | Out-Null
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $script:ServerLogSinks += [pscustomobject]@{
        Process = $process
        StdoutPath = $stdout
        StderrPath = $stderr
        StdoutTask = $stdoutTask
        StderrTask = $stderrTask
    }
    return $process
}

function Start-OnlineServer([string]$store, [string]$stdout, [string]$stderr) {
    Write-Step "Start OnlineLocal server"
    $args = @(
        "App.dll",
        "--AppType=Server",
        "--StartConfig=OnlineLocal",
        "--Console=0",
        "--OnlineStore=$store",
        "--OnlinePlayerGraceMilliseconds=1500"
    )
    Write-Host "COMMAND: dotnet $($args -join ' ')"
    $script:ServerProcess = Start-DotNetProcess $args $stdout $stderr
    $hostName, $portText = $Address.Split(":")
    Wait-Port $hostName ([int]$portText) $StartupTimeoutSeconds
    Write-Host "SERVER_PID=$($script:ServerProcess.Id)"
}

function Invoke-TestProcess([string]$mode, [string]$user, [long]$playerId) {
    Write-Step "Run OnlineTest $mode"
    $args = @(
        "App.dll",
        "--AppType=OnlineTest",
        "--Console=0",
        "--OnlineTestMode=$mode",
        "--OnlineTestAddress=$Address",
        "--OnlineTestUser=$user",
        "--OnlineTestExpectedPlayerId=$playerId",
        "--OnlineStore=$StorePath",
        "--OnlinePlayerGraceMilliseconds=1500"
    )
    Write-Host "COMMAND: dotnet $($args -join ' ')"
    $dotnet = (Get-Command dotnet).Source
    $lines = [System.Collections.Generic.List[string]]::new()
    Push-Location $BinDir
    try {
        & $dotnet @args 2>&1 | ForEach-Object {
            $line = $_.ToString()
            $lines.Add($line) | Out-Null
            Write-Host $line
        }
        $exitCode = $LASTEXITCODE
    } finally {
        Pop-Location
    }
    return [pscustomobject]@{ ExitCode = $exitCode; Output = ($lines -join [Environment]::NewLine) }
}

try {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
    $BinDir = (Resolve-Path (Join-Path $PSScriptRoot "..\Bin")).Path
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $workDir = Join-Path ([System.IO.Path]::GetTempPath()) "FirepowerLoopOnlineRoomRestart-$stamp"
    New-Item -ItemType Directory -Force -Path $workDir | Out-Null

    if ([string]::IsNullOrWhiteSpace($UserName)) {
        $UserName = "rmr$($stamp.Substring(9, 6))"
    }
    if ([string]::IsNullOrWhiteSpace($StorePath)) {
        $StorePath = "../Store/Online/identity-store-online-room-restart-$stamp.json"
    }
    $resolvedStore = [System.IO.Path]::GetFullPath((Join-Path $BinDir $StorePath))
    if (Test-Path $resolvedStore) {
        Remove-Item -LiteralPath $resolvedStore -Force
    }

    Write-Step "Online room restart test context"
    Write-Host "REPO_ROOT=$RepoRoot"
    Write-Host "BIN_DIR=$BinDir"
    Write-Host "ADDRESS=$Address"
    Write-Host "USER_NAME=$UserName"
    Write-Host "STORE_PATH=$StorePath"

    Start-OnlineServer $StorePath (Join-Path $workDir "server-prepare.out.log") (Join-Path $workDir "server-prepare.err.log")
    $prepare = Invoke-TestProcess "RoomRestartPrepare" $UserName 0
    if ($prepare.ExitCode -ne 0) { $script:Failed++ }
    Stop-OnlineServer
    Close-ServerLogSinks

    $playerMatch = [regex]::Match($prepare.Output, "ROOM_RESTART_PLAYER_ID=(\d+)")
    if (!$playerMatch.Success) {
        throw "Room restart prepare did not print player id."
    }
    $playerId = [long]$playerMatch.Groups[1].Value

    Start-OnlineServer $StorePath (Join-Path $workDir "server-check.out.log") (Join-Path $workDir "server-check.err.log")
    $check = Invoke-TestProcess "RoomRestartCheck" $UserName $playerId
    if ($check.ExitCode -ne 0) { $script:Failed++ }
    Stop-OnlineServer
    Close-ServerLogSinks

    Write-Step "Captured server output files"
    foreach ($file in Get-ChildItem -LiteralPath $workDir -File) {
        Write-Host "FILE: $($file.FullName)"
        Get-Content -LiteralPath $file.FullName -Raw | Write-Host
    }
} catch {
    Write-Host "ONLINE_ROOM_RESTART_SCRIPT_ERROR=$($_.Exception.Message)"
    Write-Host "ONLINE_ROOM_RESTART_SCRIPT_STACK=$($_.ScriptStackTrace)"
    $script:Failed++
} finally {
    Stop-OnlineServer
    Close-ServerLogSinks
    try {
        if ($resolvedStore -and (Test-Path -LiteralPath $resolvedStore)) {
            Remove-Item -LiteralPath $resolvedStore -Force
            Write-Host "STORE_CLEANUP_DELETED=$resolvedStore"
        }
    } catch {
        Write-Host "STORE_CLEANUP_FAILED=$resolvedStore error=$($_.Exception.Message)"
        $script:Failed++
    }
}

Write-Host "ONLINE_ROOM_RESTART_SCRIPT_RESULT=$(if ($script:Failed -eq 0) { 'Passed' } else { 'Failed' })"
exit $(if ($script:Failed -eq 0) { 0 } else { 1 })
