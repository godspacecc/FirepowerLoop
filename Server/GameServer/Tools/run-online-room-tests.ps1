param(
    [string]$Address = "127.0.0.1:31011",
    [string]$StorePath = "",
    [int]$StartupTimeoutSeconds = 20,
    [string[]]$Modes = @("RoomPrimary", "Chat", "ChatRetry", "RoomClosing", "RoomRetry", "RoomTimeout")
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

function Start-OnlineServer(
    [string]$store,
    [string]$stdout,
    [string]$stderr,
    [int]$failCloseCount,
    [int]$failLeaveCount,
    [int]$failConnectionSyncCount,
    [int]$failGetCurrentCount,
    [int]$failChatSyncCount,
    [int]$failChatCloseCount,
    [int]$graceMilliseconds) {
    Write-Step "Start OnlineLocal server"
    $args = @(
        "App.dll",
        "--AppType=Server",
        "--StartConfig=OnlineLocal",
        "--Console=0",
        "--OnlineStore=$store",
        "--OnlineRoomFailCloseCount=$failCloseCount",
        "--OnlineRoomFailLeaveCount=$failLeaveCount",
        "--OnlineRoomFailConnectionSyncCount=$failConnectionSyncCount",
        "--OnlineRoomFailGetCurrentCount=$failGetCurrentCount",
        "--OnlineChatFailSyncCount=$failChatSyncCount",
        "--OnlineChatFailCloseCount=$failChatCloseCount"
    )
    if ($graceMilliseconds -gt 0) {
        $args += "--OnlinePlayerGraceMilliseconds=$graceMilliseconds"
    }
    Write-Host "COMMAND: dotnet $($args -join ' ')"
    $script:ServerProcess = Start-DotNetProcess $args $stdout $stderr
    $hostName, $portText = $Address.Split(":")
    Wait-Port $hostName ([int]$portText) $StartupTimeoutSeconds
    Write-Host "SERVER_PID=$($script:ServerProcess.Id)"
}

function Get-LogFileOffsets {
    $offsets = @{}
    $logDir = Join-Path $RepoRoot "Server\GameServer\Logs"
    if (Test-Path -LiteralPath $logDir) {
        foreach ($file in Get-ChildItem -LiteralPath $logDir -File -Filter "*.log") {
            $offsets[$file.FullName] = $file.Length
        }
    }
    return $offsets
}

function Read-AppendedText([string]$path, [long]$offset) {
    $stream = [System.IO.File]::Open($path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
    try {
        if ($stream.Length -le $offset) {
            return ""
        }
        $stream.Seek($offset, [System.IO.SeekOrigin]::Begin) | Out-Null
        $reader = [System.IO.StreamReader]::new($stream, [System.Text.UTF8Encoding]::new($false), $true, 4096, $true)
        try {
            return $reader.ReadToEnd()
        } finally {
            $reader.Dispose()
        }
    } finally {
        $stream.Dispose()
    }
}

function Assert-ChatCloseRetryLogs([string[]]$paths, [hashtable]$logOffsets) {
    $text = ""
    foreach ($path in $paths) {
        if (Test-Path -LiteralPath $path) {
            $text += [System.IO.File]::ReadAllText($path)
        }
    }
    $logDir = Join-Path $RepoRoot "Server\GameServer\Logs"
    if (Test-Path -LiteralPath $logDir) {
        foreach ($file in Get-ChildItem -LiteralPath $logDir -File -Filter "*.log") {
            $offset = 0
            if ($logOffsets.ContainsKey($file.FullName)) {
                $offset = [long]$logOffsets[$file.FullName]
            }
            $text += Read-AppendedText $file.FullName $offset
        }
    }

    $injected = @(([regex]::Matches($text, "online chat close injected failure roomId=(\d+)") |
        ForEach-Object { $_.Groups[1].Value }) | Sort-Object -Unique).Count
    if ($injected -eq 0) {
        $injected = @(([regex]::Matches($text, "online room close failed stage=Close roomId=(\d+) exception=Error: 200118") |
            ForEach-Object { $_.Groups[1].Value }) | Sort-Object -Unique).Count
    }
    $removedTrue = @(([regex]::Matches($text, "online chat close confirmed roomId=(\d+) removed=true") |
        ForEach-Object { $_.Groups[1].Value }) | Sort-Object -Unique).Count
    $removedFalse = @(([regex]::Matches($text, "online chat close confirmed roomId=(\d+) removed=false") |
        ForEach-Object { $_.Groups[1].Value }) | Sort-Object -Unique).Count
    Write-Host "CHAT_CLOSE_INJECTED_FAILURE_COUNT=$injected"
    Write-Host "CHAT_CLOSE_REMOVED_TRUE_COUNT=$removedTrue"
    Write-Host "CHAT_CLOSE_REMOVED_FALSE_COUNT=$removedFalse"

    if ($injected -ne 1) {
        throw "expected exactly one chat close injected failure, got $injected"
    }
    if ($removedTrue -ne 1) {
        throw "expected exactly one chat close removed=true confirmation, got $removedTrue"
    }
    if ($removedFalse -lt 1) {
        throw "expected at least one chat close removed=false confirmation, got $removedFalse"
    }
}

function Invoke-TestProcess([string]$mode, [string]$store, [int]$graceMilliseconds) {
    Write-Step "Run OnlineTest $mode"
    $args = @(
        "App.dll",
        "--AppType=OnlineTest",
        "--Console=0",
        "--OnlineTestMode=$mode",
        "--OnlineTestAddress=$Address",
        "--OnlineStore=$store"
    )
    if ($graceMilliseconds -gt 0) {
        $args += "--OnlinePlayerGraceMilliseconds=$graceMilliseconds"
    }
    Write-Host "COMMAND: dotnet $($args -join ' ')"
    $dotnet = (Get-Command dotnet).Source
    Push-Location $BinDir
    try {
        & $dotnet @args 2>&1 | ForEach-Object { Write-Host $_.ToString() }
        return $LASTEXITCODE
    } finally {
        Pop-Location
    }
}

try {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
    $BinDir = (Resolve-Path (Join-Path $PSScriptRoot "..\Bin")).Path
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $workDir = Join-Path ([System.IO.Path]::GetTempPath()) "FirepowerLoopOnlineRoom-$stamp"
    New-Item -ItemType Directory -Force -Path $workDir | Out-Null

    if ([string]::IsNullOrWhiteSpace($StorePath)) {
        $StorePath = "../Store/Online/identity-store-online-room-$stamp.json"
    }
    $resolvedStore = [System.IO.Path]::GetFullPath((Join-Path $BinDir $StorePath))
    if (Test-Path $resolvedStore) {
        Remove-Item -LiteralPath $resolvedStore -Force
    }

    Write-Step "Online room test context"
    Write-Host "REPO_ROOT=$RepoRoot"
    Write-Host "BIN_DIR=$BinDir"
    Write-Host "ADDRESS=$Address"
    Write-Host "STORE_PATH=$StorePath"

    if ($Modes -contains "RoomPrimary") {
        Start-OnlineServer $StorePath (Join-Path $workDir "server-room-primary.out.log") (Join-Path $workDir "server-room-primary.err.log") 0 0 0 0 0 0 0
        if ((Invoke-TestProcess "RoomPrimary" $StorePath 0) -ne 0) { $script:Failed++ }
        Stop-OnlineServer
        Close-ServerLogSinks
    }

    if ($Modes -contains "RoomList") {
        Start-OnlineServer $StorePath (Join-Path $workDir "server-room-list.out.log") (Join-Path $workDir "server-room-list.err.log") 0 0 0 0 0 0 0
        if ((Invoke-TestProcess "RoomList" $StorePath 0) -ne 0) { $script:Failed++ }
        Stop-OnlineServer
        Close-ServerLogSinks
    }

    if ($Modes -contains "Chat") {
        Start-OnlineServer $StorePath (Join-Path $workDir "server-chat.out.log") (Join-Path $workDir "server-chat.err.log") 0 0 0 0 0 0 0
        if ((Invoke-TestProcess "Chat" $StorePath 0) -ne 0) { $script:Failed++ }
        Stop-OnlineServer
        Close-ServerLogSinks
    }

    if ($Modes -contains "ChatRetry") {
        Start-OnlineServer $StorePath (Join-Path $workDir "server-chat-retry.out.log") (Join-Path $workDir "server-chat-retry.err.log") 0 0 0 0 1 0 0
        if ((Invoke-TestProcess "ChatRetry" $StorePath 0) -ne 0) { $script:Failed++ }
        Stop-OnlineServer
        Close-ServerLogSinks
    }

    if ($Modes -contains "ChatCloseRetry") {
        $chatCloseOut = Join-Path $workDir "server-chat-close-retry.out.log"
        $chatCloseErr = Join-Path $workDir "server-chat-close-retry.err.log"
        $chatCloseLogOffsets = Get-LogFileOffsets
        Start-OnlineServer $StorePath $chatCloseOut $chatCloseErr 1 0 0 0 0 1 0
        if ((Invoke-TestProcess "ChatCloseRetry" $StorePath 0) -ne 0) { $script:Failed++ }
        Stop-OnlineServer
        Close-ServerLogSinks
        Assert-ChatCloseRetryLogs @($chatCloseOut, $chatCloseErr) $chatCloseLogOffsets
    }

    if ($Modes -contains "RoomClosing") {
        Start-OnlineServer $StorePath (Join-Path $workDir "server-room-closing.out.log") (Join-Path $workDir "server-room-closing.err.log") 1 0 0 0 0 0 0
        if ((Invoke-TestProcess "RoomClosing" $StorePath 0) -ne 0) { $script:Failed++ }
        Stop-OnlineServer
        Close-ServerLogSinks
    }

    if ($Modes -contains "RoomRetry") {
        Start-OnlineServer $StorePath (Join-Path $workDir "server-room-retry.out.log") (Join-Path $workDir "server-room-retry.err.log") 0 1 1 1 0 0 0
        if ((Invoke-TestProcess "RoomRetry" $StorePath 0) -ne 0) { $script:Failed++ }
        Stop-OnlineServer
        Close-ServerLogSinks
    }

    if ($Modes -contains "RoomTimeout") {
        Start-OnlineServer $StorePath (Join-Path $workDir "server-room-timeout.out.log") (Join-Path $workDir "server-room-timeout.err.log") 0 0 0 0 0 0 0
        if ((Invoke-TestProcess "RoomTimeout" $StorePath 0) -ne 0) { $script:Failed++ }
        Stop-OnlineServer
        Close-ServerLogSinks
    }

    Write-Step "Captured server output files"
    foreach ($file in Get-ChildItem -LiteralPath $workDir -File) {
        Write-Host "FILE: $($file.FullName)"
        Get-Content -LiteralPath $file.FullName -Raw | Write-Host
    }
} catch {
    Write-Host "ONLINE_ROOM_SCRIPT_ERROR=$($_.Exception.Message)"
    Write-Host "ONLINE_ROOM_SCRIPT_STACK=$($_.ScriptStackTrace)"
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

Write-Host "ONLINE_ROOM_SCRIPT_RESULT=$(if ($script:Failed -eq 0) { 'Passed' } else { 'Failed' })"
exit $(if ($script:Failed -eq 0) { 0 } else { 1 })
