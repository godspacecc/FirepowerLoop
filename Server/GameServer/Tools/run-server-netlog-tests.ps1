param(
    [string]$ServerRoot = (Join-Path $PSScriptRoot ".."),
    [int]$StartupTimeoutSeconds = 25,
    [int]$TestPort = 31011
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$script:failed = 0
$script:launcher = $null
$script:launcherChildPids = [System.Collections.Generic.HashSet[int]]::new()
$script:stdoutLines = [System.Collections.Generic.List[string]]::new()
$script:stderrLines = [System.Collections.Generic.List[string]]::new()
$script:stdoutTask = $null
$script:stderrTask = $null
$script:tempRoot = $null
$script:launcherExitCode = "NotExited"

[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$OutputEncoding = [System.Text.UTF8Encoding]::new($false)

function Write-Assertion([string]$Name, [bool]$Passed) {
    if ($Passed) {
        Write-Host "$Name=Passed"
    }
    else {
        Write-Host "$Name=Failed"
        $script:failed++
    }
}

function Join-ProcessArguments([string[]]$Arguments) {
    return (($Arguments | ForEach-Object {
        if ($_ -match '[\s"]') { '"' + $_.Replace('"', '\"') + '"' } else { $_ }
    }) -join ' ')
}

function Test-PortFree([int]$Port) {
    $listener = $null
    try {
        $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $Port)
        $listener.Server.ExclusiveAddressUse = $true
        $listener.Start()
        return $true
    }
    catch {
        return $false
    }
    finally {
        if ($null -ne $listener) { $listener.Stop() }
    }
}

function Pump-LauncherOutput {
    foreach ($stream in @("stdout", "stderr")) {
        $taskName = "${stream}Task"
        $task = Get-Variable -Scope Script -Name $taskName -ValueOnly
        if ($null -eq $task -or -not $task.IsCompleted) { continue }
        $line = $task.GetAwaiter().GetResult()
        if ($null -eq $line) {
            Set-Variable -Scope Script -Name $taskName -Value $null
            continue
        }
        if ($stream -eq "stdout") { [void]$script:stdoutLines.Add($line) } else { [void]$script:stderrLines.Add($line) }
        $reader = if ($stream -eq "stdout") { $script:launcher.StandardOutput } else { $script:launcher.StandardError }
        Set-Variable -Scope Script -Name $taskName -Value ($reader.ReadLineAsync())
    }
}

function Get-LauncherText([int]$StartIndex = 0) {
    Pump-LauncherOutput
    if ($StartIndex -ge $script:stdoutLines.Count) { return "" }
    return (($script:stdoutLines.GetRange($StartIndex, $script:stdoutLines.Count - $StartIndex)) -join [Environment]::NewLine)
}

function Wait-LauncherText([string]$Pattern, [int]$StartIndex, [int]$TimeoutSeconds = $StartupTimeoutSeconds) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        Pump-LauncherOutput
        $text = Get-LauncherText $StartIndex
        if ($text -match $Pattern) { return $text }
        if ($script:launcher.HasExited) { throw "launcher exited unexpectedly: $($script:launcher.ExitCode)" }
        Start-Sleep -Milliseconds 100
    }
    throw "timed out waiting for launcher output"
}

function Wait-PortOpen([int]$Port, [int]$TimeoutSeconds = $StartupTimeoutSeconds) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $client = $null
        try {
            $client = [System.Net.Sockets.TcpClient]::new()
            $connect = $client.ConnectAsync("127.0.0.1", $Port)
            if ($connect.Wait(250) -and $client.Connected) { return }
        }
        catch {
        }
        finally {
            if ($null -ne $client) { $client.Dispose() }
        }
        Start-Sleep -Milliseconds 150
    }
    throw "server port did not open"
}

function Test-PortOpen([int]$Port) {
    $client = $null
    try {
        $client = [System.Net.Sockets.TcpClient]::new()
        $connect = $client.ConnectAsync("127.0.0.1", $Port)
        return ($connect.Wait(500) -and $client.Connected)
    }
    catch {
        return $false
    }
    finally {
        if ($null -ne $client) { $client.Dispose() }
    }
}

function Send-LauncherLine([string]$Line) {
    $script:launcher.StandardInput.WriteLine($Line)
    $script:launcher.StandardInput.Flush()
}

function Get-LatestServerPid {
    Pump-LauncherOutput
    for ($i = $script:stdoutLines.Count - 1; $i -ge 0; $i--) {
        $match = [regex]::Match($script:stdoutLines[$i], "服务器进程已启动：PID=(\d+)")
        if ($match.Success) { return [int]$match.Groups[1].Value }
    }
    throw "server PID was not found in launcher output"
}

function Read-SharedText([string]$Path) {
    $stream = [System.IO.FileStream]::new($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
    $reader = [System.IO.StreamReader]::new($stream, [System.Text.Encoding]::UTF8, $true)
    try { return $reader.ReadToEnd() } finally { $reader.Dispose(); $stream.Dispose() }
}

function Get-DebugSnapshot {
    $snapshot = @{}
    $logs = Join-Path $script:tempRoot "Logs"
    if (Test-Path -LiteralPath $logs) {
        foreach ($file in Get-ChildItem -LiteralPath $logs -File -Filter "*.Debug.log") {
            $snapshot[$file.FullName] = Read-SharedText $file.FullName
        }
    }
    return $snapshot
}

function Get-DebugDelta([hashtable]$Before) {
    $parts = [System.Collections.Generic.List[string]]::new()
    $logs = Join-Path $script:tempRoot "Logs"
    if (-not (Test-Path -LiteralPath $logs)) { return "" }
    foreach ($file in Get-ChildItem -LiteralPath $logs -File -Filter "*.Debug.log") {
        $current = Read-SharedText $file.FullName
        $prior = if ($Before.ContainsKey($file.FullName)) { [string]$Before[$file.FullName] } else { "" }
        if ($current.Length -ge $prior.Length) { $parts.Add($current.Substring($prior.Length)) }
    }
    return ($parts -join [Environment]::NewLine)
}

function Invoke-LoginEntry([string]$UserName) {
    $bin = Join-Path $script:tempRoot "Bin"
    $store = Join-Path $script:tempRoot "Store\Online\identity-store.json"
    $arguments = @(
        "App.dll", "--AppType=OnlineTest", "--Console=0", "--OnlineTestMode=LoginEntryOnly",
        "--OnlineTestAddress=127.0.0.1:$TestPort", "--OnlineTestUser=$UserName", "--OnlineStore=$store"
    )
    $info = [System.Diagnostics.ProcessStartInfo]::new()
    $info.FileName = (Get-Command dotnet).Source
    $info.WorkingDirectory = $bin
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.Arguments = Join-ProcessArguments $arguments
    $process = [System.Diagnostics.Process]::Start($info)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    $deadline = (Get-Date).AddSeconds(30)
    while (-not $process.HasExited -and (Get-Date) -lt $deadline) {
        Pump-LauncherOutput
        Start-Sleep -Milliseconds 50
    }
    if (-not $process.HasExited) {
        $process.Kill()
        $process.WaitForExit()
        throw "OnlineTest timed out"
    }
    [void]$stdout.GetAwaiter().GetResult()
    [void]$stderr.GetAwaiter().GetResult()
    $exitCode = $process.ExitCode
    $process.Dispose()
    return $exitCode
}

function Wait-StageEvidence([hashtable]$DebugBefore, [int]$OutputStart, [string]$Pattern, [int]$TimeoutSeconds = 8) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        Pump-LauncherOutput
        $window = Get-LauncherText $OutputStart
        $debug = Get-DebugDelta $DebugBefore
        if ($window -match $Pattern -and $debug -match $Pattern) {
            return [pscustomobject]@{ Window = $window; Debug = $debug }
        }
        Start-Sleep -Milliseconds 150
    }
    return [pscustomobject]@{ Window = (Get-LauncherText $OutputStart); Debug = (Get-DebugDelta $DebugBefore) }
}

function Stop-TestProcesses {
    Pump-LauncherOutput
    foreach ($line in $script:stdoutLines) {
        $match = [regex]::Match($line, "服务器进程已启动：PID=(\d+)")
        if ($match.Success) { [void]$script:launcherChildPids.Add([int]$match.Groups[1].Value) }
    }
    if ($null -ne $script:launcher) {
        try { $script:launcher.StandardInput.Close() } catch {}
    }
    foreach ($pidValue in $script:launcherChildPids) {
        try { Stop-Process -Id $pidValue -Force -ErrorAction Stop } catch {}
    }
    if ($null -ne $script:tempRoot) {
        foreach ($dotnetProcess in Get-Process dotnet -ErrorAction SilentlyContinue) {
            try {
                if ($dotnetProcess.Modules.FileName -like "$script:tempRoot*") {
                    Stop-Process -Id $dotnetProcess.Id -Force -ErrorAction Stop
                    [void]$dotnetProcess.WaitForExit(5000)
                }
            }
            catch {
            }
        }
    }
    if ($null -ne $script:launcher) {
        try {
            if (-not $script:launcher.HasExited) { $script:launcher.Kill() }
            [void]$script:launcher.WaitForExit(5000)
            if ($script:launcher.HasExited) { $script:launcherExitCode = $script:launcher.ExitCode }
        }
        catch {
        }
        $script:launcher.Dispose()
        $script:launcher = $null
    }
}

try {
    $resolvedRoot = [System.IO.Path]::GetFullPath($ServerRoot)
    if ($TestPort -ne 31011) { throw "OnlineLocal 当前配置端口固定为 31011；TestPort 必须为 31011。" }
    if (-not (Test-PortFree $TestPort)) { throw "test port is occupied" }

    $script:tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("FirepowerLoop-NetLog-" + [Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $script:tempRoot | Out-Null
    Copy-Item -LiteralPath (Join-Path $resolvedRoot "Bin") -Destination (Join-Path $script:tempRoot "Bin") -Recurse
    Copy-Item -LiteralPath (Join-Path $resolvedRoot "Config") -Destination (Join-Path $script:tempRoot "Config") -Recurse
    New-Item -ItemType Directory -Path (Join-Path $script:tempRoot "Tools") | Out-Null
    Copy-Item -LiteralPath (Join-Path $resolvedRoot "Tools\start-local-online-server.ps1") -Destination (Join-Path $script:tempRoot "Tools\start-local-online-server.ps1")

    Write-Assertion "TEMP_ROOT_ISOLATED" ($script:tempRoot -ne $resolvedRoot -and $script:tempRoot -notlike "$resolvedRoot*")

    $info = [System.Diagnostics.ProcessStartInfo]::new()
    $info.FileName = "powershell.exe"
    $info.WorkingDirectory = $script:tempRoot
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardInput = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.Arguments = Join-ProcessArguments @("-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", (Join-Path $script:tempRoot "Tools\start-local-online-server.ps1"), "-GameServerRoot", $script:tempRoot, "-Port", "$TestPort")
    $script:launcher = [System.Diagnostics.Process]::Start($info)
    $script:stdoutTask = $script:launcher.StandardOutput.ReadLineAsync()
    $script:stderrTask = $script:launcher.StandardError.ReadLineAsync()
    $startup = Wait-LauncherText "服务器进程已启动：PID=(\d+)" 0
    Wait-PortOpen $TestPort
    $firstPid = Get-LatestServerPid
    [void]$script:launcherChildPids.Add($firstPid)

    $start = $script:stdoutLines.Count
    Send-LauncherLine "netlog status"
    $status = Wait-LauncherText "完整协议日志：已关闭" $start
    Write-Assertion "INITIAL_STATUS_OFF" ($status -match "已关闭")

    $passwordPatterns = @("Pass_123", "Wrong_99", "123456789", "abc def")
    $beforeDebug = Get-DebugSnapshot
    $start = $script:stdoutLines.Count
    $closedExit = Invoke-LoginEntry "nlclosed$([DateTime]::UtcNow.ToString('HHmmss'))"
    Start-Sleep -Milliseconds 800
    $closedWindow = Get-LauncherText $start
    $closedDebug = Get-DebugDelta $beforeDebug
    $closedHasPassword = @($passwordPatterns | Where-Object { $closedWindow.Contains($_) -or $closedDebug.Contains($_) })
    Write-Assertion "CLOSED_LOGIN_EXIT" ($closedExit -eq 0)
    Write-Assertion "CLOSED_PASSWORD_ABSENT_WINDOW_DEBUG" ($closedHasPassword.Count -eq 0)

    $start = $script:stdoutLines.Count
    Send-LauncherLine "netlog on"
    $onStatus = Wait-LauncherText "不会删除历史日志" $start
    Write-Assertion "ON_WARNING" ($onStatus -match "密码等凭据" -and $onStatus -match "Debug" -and $onStatus -match "不会删除历史日志")

    $beforeDebug = Get-DebugSnapshot
    $start = $script:stdoutLines.Count
    $onExit = Invoke-LoginEntry "nlon$([DateTime]::UtcNow.ToString('HHmmss'))"
    $evidence = Wait-StageEvidence $beforeDebug $start "Pass_123"
    $contextPattern = "\[Protocol\].*(Send|Receive).*Session=.*Remote=.*Type=.*C2G_Login"
    Write-Assertion "ON_LOGIN_EXIT" ($onExit -eq 0)
    Write-Assertion "ON_PASSWORD_IN_WINDOW" ($evidence.Window -match "Pass_123")
    Write-Assertion "ON_PASSWORD_IN_DEBUG" ($evidence.Debug -match "Pass_123")
    Write-Assertion "ON_CONTEXT_IN_WINDOW" ($evidence.Window -match $contextPattern)
    Write-Assertion "ON_CONTEXT_IN_DEBUG" ($evidence.Debug -match $contextPattern)
    Write-Assertion "ON_DIRECTIONS" ($evidence.Window -match "\[Protocol\] Receive" -and $evidence.Window -match "\[Protocol\] Send")

    $start = $script:stdoutLines.Count
    Send-LauncherLine "netlog off"
    [void](Wait-LauncherText "完整协议日志：已关闭" $start)
    $beforeDebug = Get-DebugSnapshot
    $start = $script:stdoutLines.Count
    $offExit = Invoke-LoginEntry "nloff$([DateTime]::UtcNow.ToString('HHmmss'))"
    Start-Sleep -Milliseconds 800
    $offWindow = Get-LauncherText $start
    $offDebug = Get-DebugDelta $beforeDebug
    $offHasPassword = @($passwordPatterns | Where-Object { $offWindow.Contains($_) -or $offDebug.Contains($_) })
    Write-Assertion "OFF_LOGIN_EXIT" ($offExit -eq 0)
    Write-Assertion "OFF_PASSWORD_ABSENT_WINDOW_DEBUG" ($offHasPassword.Count -eq 0)

    $start = $script:stdoutLines.Count
    Send-LauncherLine "netlog invalid extra"
    [void](Wait-LauncherText "用法：netlog on \| netlog off \| netlog status" $start)
    Send-LauncherLine "netlog status"
    $invalidStatus = Wait-LauncherText "完整协议日志：已关闭" $start
    Write-Assertion "INVALID_USAGE_AND_STATE" ($invalidStatus -match "用法：netlog" -and $invalidStatus -match "已关闭")

    $start = $script:stdoutLines.Count
    Send-LauncherLine "R"
    $restart = Wait-LauncherText "服务器进程已启动：PID=(\d+)" $start
    Wait-PortOpen $TestPort
    $secondPid = Get-LatestServerPid
    [void]$script:launcherChildPids.Add($secondPid)
    Write-Assertion "RESTART_NEW_PID" ($secondPid -ne $firstPid)
    $start = $script:stdoutLines.Count
    Send-LauncherLine "netlog status"
    $restartStatus = Wait-LauncherText "完整协议日志：已关闭" $start
    Write-Assertion "RESTART_STATUS_OFF" ($restartStatus -match "已关闭")

    $script:launcher.StandardInput.Close()
    Start-Sleep -Milliseconds 500
    $serverAfterEof = Get-Process -Id $secondPid -ErrorAction SilentlyContinue
    Write-Assertion "REDIRECTED_EOF_LAUNCHER_CONTINUES" (-not $script:launcher.HasExited)
    Write-Assertion "REDIRECTED_EOF_SERVER_PROCESS_CONTINUES" ($null -ne $serverAfterEof -and -not $serverAfterEof.HasExited)
    Write-Assertion "REDIRECTED_EOF_PORT_CONTINUES" (Test-PortOpen $TestPort)
}
catch {
    Write-Host "TEST_EXECUTION=Failed $($_.Exception.GetType().Name) Line=$($_.InvocationInfo.ScriptLineNumber)"
    $script:failed++
}
finally {
    Stop-TestProcesses
    $portReleased = $false
    for ($i = 0; $i -lt 50; $i++) {
        if (Test-PortFree $TestPort) { $portReleased = $true; break }
        Start-Sleep -Milliseconds 100
    }
    Write-Assertion "NO_REMAINING_PROCESS_OR_PORT" $portReleased
    if ($null -ne $script:tempRoot -and (Test-Path -LiteralPath $script:tempRoot)) {
        for ($i = 0; $i -lt 20; $i++) {
            try {
                Remove-Item -LiteralPath $script:tempRoot -Recurse -Force -ErrorAction Stop
                break
            }
            catch {
                Start-Sleep -Milliseconds 250
            }
        }
        Write-Assertion "TEMP_ROOT_REMOVED" (-not (Test-Path -LiteralPath $script:tempRoot))
    }
    $result = if ($script:failed -eq 0) { "Passed" } else { "Failed" }
    Write-Host "LAUNCHER_EXIT_CODE=$script:launcherExitCode"
    Write-Host "SERVER_NETLOG_TEST_RESULT=$result"
    if ($script:failed -eq 0) { exit 0 } else { exit 1 }
}
