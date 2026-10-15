param(
    [string]$Address = "127.0.0.1:31011",
    [string]$UserName = "",
    [string]$StorePath = "",
    [int]$StartupTimeoutSeconds = 20,
    [switch]$StartStopSelfCheck,
    [switch]$ReplaceQuickOnly,
    [switch]$RetrySaveQuickOnly,
    [switch]$LoginEntryOnly
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$script:ServerProcess = $null
$script:Failed = 0
$script:ServerLogSinks = @()
$script:CreatedStorePaths = [System.Collections.Generic.List[string]]::new()
$script:CreatedDirectories = [System.Collections.Generic.List[string]]::new()

[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$OutputEncoding = [System.Text.UTF8Encoding]::new($false)

function Write-Step([string]$name) {
    Write-Host ""
    Write-Host "===== $name ====="
}

function Get-ScriptResult {
    if ($script:Failed -eq 0) {
        return "Passed"
    }

    return "Failed"
}

function Get-ScriptExitCode {
    if ($script:Failed -eq 0) {
        return 0
    }

    return 1
}

function Assert-ScriptCondition([bool]$condition, [string]$passName, [string]$failMessage) {
    if ($condition) {
        Write-Host "$passName=Passed"
    } else {
        Write-Host "$passName=Failed $failMessage"
        $script:Failed++
    }
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

function Flush-ServerLogSinks {
    Close-ServerLogSinks
}

function Write-ServerLogFile([string]$label, [string]$path) {
    Write-Host "$label=$path"
    if (Test-Path -LiteralPath $path) {
        Get-Content -LiteralPath $path | ForEach-Object { Write-Host "$label> $_" }
    } else {
        Write-Host "$label> <missing>"
    }
}

function Stop-Flush-And-PrintServerLogs([string]$stdout, [string]$stderr) {
    Stop-OnlineServer
    Flush-ServerLogSinks
    Write-ServerLogFile "SERVER_STDOUT" $stdout
    Write-ServerLogFile "SERVER_STDERR" $stderr
}

function Wait-StoreFile([string]$store, [string]$stdout, [string]$stderr, [int]$timeoutSeconds) {
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-Path -LiteralPath $store -PathType Leaf) {
            return
        }

        if ($script:ServerProcess -and $script:ServerProcess.HasExited) {
            $exitCode = $script:ServerProcess.ExitCode
            Stop-Flush-And-PrintServerLogs $stdout $stderr
            throw "Server exited before creating Store file '$store'. ExitCode=$exitCode"
        }

        Start-Sleep -Milliseconds 300
    }

    Stop-Flush-And-PrintServerLogs $stdout $stderr
    throw "Timed out waiting for Store file '$store' to be created."
}

function Read-StoreBytes([string]$store, [int]$timeoutSeconds) {
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    $lastError = $null
    while ((Get-Date) -lt $deadline) {
        try {
            return [System.IO.File]::ReadAllBytes($store)
        } catch {
            $lastError = $_.Exception.Message
            Start-Sleep -Milliseconds 100
        }
    }

    throw "Timed out reading Store file '$store'. LastError=$lastError"
}

function Count-TextMatches([string]$path, [string]$pattern) {
    if (-not (Test-Path -LiteralPath $path)) {
        return 0
    }
    $matches = Select-String -LiteralPath $path -Pattern $pattern -AllMatches
    if ($null -eq $matches) {
        return 0
    }
    return ($matches | ForEach-Object { $_.Matches.Count } | Measure-Object -Sum).Sum
}

function Get-StoreTempFiles([string]$path) {
    $parent = [System.IO.Path]::GetDirectoryName($path)
    $leaf = [System.IO.Path]::GetFileName($path)
    if (-not (Test-Path -LiteralPath $parent)) {
        return @()
    }
    return @(Get-ChildItem -LiteralPath $parent -File -Filter "$leaf.*.tmp" -ErrorAction SilentlyContinue)
}

function Clear-StoreTempFiles([string]$path) {
    foreach ($file in Get-StoreTempFiles $path) {
        Remove-Item -LiteralPath $file.FullName -Force
        Write-Host "STORE_TEMP_CLEANUP_DELETED=$($file.FullName)"
    }
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
        if ($_ -match '\s') {
            '"' + $_.Replace('"', '\"') + '"'
        } else {
            $_
        }
    }) -join " ")

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $psi
    $stdoutTask = $null
    $stderrTask = $null
    $started = $false
    try {
        New-Item -ItemType Directory -Force -Path ([System.IO.Path]::GetDirectoryName($stdout)) | Out-Null
        New-Item -ItemType Directory -Force -Path ([System.IO.Path]::GetDirectoryName($stderr)) | Out-Null
        $process.Start() | Out-Null
        $started = $true
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
    } catch {
        if ($started -and -not $process.HasExited) {
            Stop-Process -Id $process.Id -Force
            $process.WaitForExit(5000) | Out-Null
        }
        if ($stdoutTask) {
            try { [System.IO.File]::WriteAllText($stdout, $stdoutTask.Result, [System.Text.UTF8Encoding]::new($false)) } catch {}
        }
        if ($stderrTask) {
            try { [System.IO.File]::WriteAllText($stderr, $stderrTask.Result, [System.Text.UTF8Encoding]::new($false)) } catch {}
        }
        throw
    }
}

function Start-OnlineServer([string]$store, [string]$stdout, [string]$stderr) {
    Write-Step "Start OnlineLocal server"
    $args = @(
        "App.dll",
        "--AppType=Server",
        "--StartConfig=OnlineLocal",
        "--Console=0",
        "--OnlineStore=$store"
    )
    Write-Host "COMMAND: dotnet $($args -join ' ')"
    $script:ServerProcess = Start-DotNetProcess $args $stdout $stderr
    $hostName, $portText = $Address.Split(":")
    Wait-Port $hostName ([int]$portText) $StartupTimeoutSeconds
    Write-Host "SERVER_PID=$($script:ServerProcess.Id)"
}

function Invoke-TestProcess([string]$mode, [string]$user, [long]$accountId, [long]$playerId, [string]$store) {
    Write-Step "Run OnlineTest $mode"
    $args = @(
        "App.dll",
        "--AppType=OnlineTest",
        "--Console=0",
        "--OnlineTestMode=$mode",
        "--OnlineTestAddress=$Address",
        "--OnlineTestUser=$user",
        "--OnlineTestExpectedAccountId=$accountId",
        "--OnlineTestExpectedPlayerId=$playerId",
        "--OnlineStore=$store"
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
    Write-Host "EXIT_CODE=$exitCode"
    return [pscustomobject]@{ ExitCode = $exitCode; Output = ($lines -join [Environment]::NewLine) }
}

try {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
    $BinDir = (Resolve-Path (Join-Path $PSScriptRoot "..\Bin")).Path
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $workDir = Join-Path ([System.IO.Path]::GetTempPath()) "AnotherDayOnlineLifecycle-$stamp"
    New-Item -ItemType Directory -Force -Path $workDir | Out-Null
    $script:CreatedDirectories.Add($workDir) | Out-Null

    if ([string]::IsNullOrWhiteSpace($UserName)) {
        $UserName = "ad_$($stamp.Replace('-', ''))"
    }
    if ([string]::IsNullOrWhiteSpace($StorePath)) {
        $StorePath = "../Store/Online/identity-store-online-lifecycle-$stamp.json"
    }

    $resolvedStore = [System.IO.Path]::GetFullPath((Join-Path $BinDir $StorePath))
    $script:CreatedStorePaths.Add($resolvedStore) | Out-Null
    if (Test-Path $resolvedStore) {
        Remove-Item -LiteralPath $resolvedStore -Force
    }

    Write-Step "Online lifecycle test context"
    Write-Host "REPO_ROOT=$RepoRoot"
    Write-Host "BIN_DIR=$BinDir"
    Write-Host "ADDRESS=$Address"
    Write-Host "USER_NAME=$UserName"
    Write-Host "STORE_PATH=$StorePath"

    if ($StartStopSelfCheck) {
        $serverOut = Join-Path $workDir "server-selfcheck.out.log"
        $serverErr = Join-Path $workDir "server-selfcheck.err.log"
        Start-OnlineServer $StorePath $serverOut $serverErr
        $selfCheckPid = $script:ServerProcess.Id
        Write-Host "SELF_CHECK_LISTEN_PID=$selfCheckPid"
        Stop-OnlineServer
        Close-ServerLogSinks
        Start-Sleep -Seconds 1
        $hostName, $portText = $Address.Split(":")
        try {
            $client = [System.Net.Sockets.TcpClient]::new()
            $task = $client.ConnectAsync($hostName, [int]$portText)
            $stillListening = $task.Wait(300) -and $client.Connected
            $client.Dispose()
        } catch {
            $stillListening = $false
        }
        if ($stillListening) {
            Write-Host "SELF_CHECK_CLEANUP=Failed"
            exit 1
        }
        Write-Host "SELF_CHECK_CLEANUP=Passed"
        exit 0
    }

    if ($ReplaceQuickOnly) {
        $serverOut = Join-Path $workDir "server-replace-quick.out.log"
        $serverErr = Join-Path $workDir "server-replace-quick.err.log"
        Start-OnlineServer $StorePath $serverOut $serverErr
        $quick = Invoke-TestProcess "ReplaceQuick" $UserName 0 0 $StorePath
        if ($quick.ExitCode -ne 0) { $script:Failed++ }
        Write-Step "Captured server output files"
        foreach ($file in Get-ChildItem -LiteralPath $workDir -File) {
            Write-Host "FILE: $($file.FullName)"
            Get-Content -LiteralPath $file.FullName -Raw | Write-Host
        }
        Write-Host "ONLINE_LIFECYCLE_SCRIPT_RESULT=$(Get-ScriptResult)"
        exit (Get-ScriptExitCode)
    }

    if ($RetrySaveQuickOnly) {
        $retryUser = "adr$($stamp.Substring(9, 6))"

        $serverOut = Join-Path $workDir "server-retry-prepare.out.log"
        $serverErr = Join-Path $workDir "server-retry-prepare.err.log"
        Start-OnlineServer $StorePath $serverOut $serverErr
        $prepare = Invoke-TestProcess "RetryPrepare" $retryUser 0 0 $StorePath
        if ($prepare.ExitCode -ne 0) { $script:Failed++ }
        Stop-OnlineServer
        Flush-ServerLogSinks

        $serverOut = Join-Path $workDir "server-retry-failure.out.log"
        $serverErr = Join-Path $workDir "server-retry-failure.err.log"
        Start-OnlineServer $StorePath $serverOut $serverErr
        $lock = [System.IO.File]::Open($resolvedStore, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
        try {
            $failure = Invoke-TestProcess "RetryFailureQuick" $retryUser 0 0 $StorePath
            if ($failure.ExitCode -ne 0) { $script:Failed++ }
            Start-Sleep -Seconds 12
        } finally {
            $lock.Dispose()
        }
        Stop-OnlineServer
        Flush-ServerLogSinks
        $failureAttempts = @(Get-StoreTempFiles $resolvedStore).Count
        Write-Host "RETRY_FAILURE_SAVE_ATTEMPTS=$failureAttempts"
        if ($failureAttempts -lt 3) {
            Write-Host "RETRY-FAILURE | Failed | 0ms | expected at least 3 save attempts"
            $script:Failed++
        } else {
            Write-Host "RETRY-FAILURE | Passed | 0ms |"
        }
        Clear-StoreTempFiles $resolvedStore

        $serverOut = Join-Path $workDir "server-retry-cancel.out.log"
        $serverErr = Join-Path $workDir "server-retry-cancel.err.log"
        Start-OnlineServer $StorePath $serverOut $serverErr
        $lock = [System.IO.File]::Open($resolvedStore, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
        try {
            $cancel = Invoke-TestProcess "RetryCancelQuick" $retryUser 0 0 $StorePath
            if ($cancel.ExitCode -ne 0) { $script:Failed++ }
        } finally {
            $lock.Dispose()
        }
        Stop-OnlineServer
        Flush-ServerLogSinks
        $cancelAttempts = @(Get-StoreTempFiles $resolvedStore).Count
        Write-Host "RETRY_CANCEL_SAVE_ATTEMPTS=$cancelAttempts"
        if ($cancelAttempts -ne 1) {
            Write-Host "RETRY-CANCEL | Failed | 0ms | expected exactly 1 old-epoch save attempt"
            $script:Failed++
        } else {
            Write-Host "RETRY-CANCEL | Passed | 0ms |"
        }

        Write-Step "Captured server output files"
        foreach ($file in Get-ChildItem -LiteralPath $workDir -File) {
            Write-Host "FILE: $($file.FullName)"
            Get-Content -LiteralPath $file.FullName -Raw | Write-Host
        }
        Write-Host "ONLINE_LIFECYCLE_SCRIPT_RESULT=$(Get-ScriptResult)"
        exit (Get-ScriptExitCode)
    }

    if ($LoginEntryOnly) {
        # === 阶段 1: LOGIN-AUTO-01 至 04 使用独立临时 Store ===
        $loginWorkDir = Join-Path $workDir "login-entry-only"
        New-Item -ItemType Directory -Force -Path $loginWorkDir | Out-Null
        $resolvedLoginStore = [System.IO.Path]::GetFullPath((Join-Path $loginWorkDir "login-entry-store.json"))
        $script:CreatedStorePaths.Add($resolvedLoginStore) | Out-Null

        $loginServerOut = Join-Path $loginWorkDir "server-login-entry.out.log"
        $loginServerErr = Join-Path $loginWorkDir "server-login-entry.err.log"
        Start-OnlineServer $resolvedLoginStore $loginServerOut $loginServerErr
        $loginEntry = Invoke-TestProcess "LoginEntry" $UserName 0 0 $resolvedLoginStore
        if ($loginEntry.ExitCode -ne 0) { $script:Failed++ }
        Stop-OnlineServer
        Flush-ServerLogSinks

        # === 阶段 2: LOGIN-AUTO-05 锁失败验证，使用隔离 Store ===
        Write-Step "LOGIN-AUTO-05 - exclusive file lock failure"
        $lockWorkDir = Join-Path $workDir "login-lock-fail"
        New-Item -ItemType Directory -Force -Path $lockWorkDir | Out-Null
        $resolvedLockStore = [System.IO.Path]::GetFullPath((Join-Path $lockWorkDir "lock-fail-store.json"))
        $script:CreatedStorePaths.Add($resolvedLockStore) | Out-Null

        $lockServerOut = Join-Path $lockWorkDir "server-lock-fail.out.log"
        $lockServerErr = Join-Path $lockWorkDir "server-lock-fail.err.log"
        # 通过服务器自身加载路径创建有效空 Store，避免脚本猜测 JSON 格式。
        Start-Sleep -Milliseconds 300
        Start-OnlineServer $resolvedLockStore $lockServerOut $lockServerErr
        Wait-StoreFile $resolvedLockStore $lockServerOut $lockServerErr $StartupTimeoutSeconds

        # 加锁前读取 Store 基线字节
        $lockBaseline = Read-StoreBytes $resolvedLockStore $StartupTimeoutSeconds
        Write-Host "LOCK_BASELINE_SIZE=$($lockBaseline.Length)"

        # 使用 FileShare.None 独占 Store
        $lockUser = "locktest_$(Get-Date -Format 'HHmmss')$(Get-Random -Maximum 9999)"
        $lockFileStream = $null
        try {
            $lockFileStream = [System.IO.File]::Open($resolvedLockStore, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::None)
            # 锁内只调用 LoginEntryCommitFailure
            $lockTest = Invoke-TestProcess "LoginEntryCommitFailure" $lockUser 0 0 $resolvedLockStore
            if ($lockTest.ExitCode -ne 0) { $script:Failed++ }
        } finally {
            if ($lockFileStream) {
                $lockFileStream.Dispose()
            }
        }

        # 释放锁后读取 Store，验证字节不变
        $lockAfterBytes = [byte[]]@()
        $lockAfterBytes = Read-StoreBytes $resolvedLockStore $StartupTimeoutSeconds
        $lockBytesMatch = [System.Convert]::ToBase64String($lockBaseline) -eq [System.Convert]::ToBase64String($lockAfterBytes)
        if (-not $lockBytesMatch) {
            Write-Host "LOCK_STORE_CHANGED=Failed"
            $script:Failed++
        } else {
            Write-Host "LOCK_STORE_UNCHANGED=Passed"
        }

        # 检查无残留临时文件
        $lockParent = [System.IO.Path]::GetDirectoryName($resolvedLockStore)
        $lockLeaf = [System.IO.Path]::GetFileName($resolvedLockStore)
        $lockTemps = @(Get-ChildItem -LiteralPath $lockParent -File -Filter "$lockLeaf.*.tmp" -ErrorAction SilentlyContinue)
        if ($lockTemps.Count -gt 0) {
            Write-Host "LOCK_LEAKED_TEMPS=$($lockTemps.Count)"
            $script:Failed++
        } else {
            Write-Host "LOCK_NO_LEAKED_TEMPS=Passed"
        }
        Stop-OnlineServer
        Flush-ServerLogSinks

        # === 阶段 3: LOGIN-AUTO-06 解锁后同账号重试 ===
        Write-Step "LOGIN-AUTO-06 - retry after unlock"
        $retryServerOut = Join-Path $lockWorkDir "server-lock-retry.out.log"
        $retryServerErr = Join-Path $lockWorkDir "server-lock-retry.err.log"
        Start-Sleep -Milliseconds 300
        Start-OnlineServer $resolvedLockStore $retryServerOut $retryServerErr
        $retryTest = Invoke-TestProcess "LoginEntryRetry" $lockUser 0 0 $resolvedLockStore
        if ($retryTest.ExitCode -ne 0) { $script:Failed++ }
        Stop-OnlineServer
        Flush-ServerLogSinks

        # 验证最终仅一个账号和一个 Player，且失败提交没有提前递增 ID。
        Assert-ScriptCondition (Test-Path -LiteralPath $resolvedLockStore) "LOCK_RETRY_STORE" "missing=$resolvedLockStore"
        if (Test-Path -LiteralPath $resolvedLockStore) {
            $finalStoreJson = [System.IO.File]::ReadAllText($resolvedLockStore, [System.Text.UTF8Encoding]::new($false))
            $finalParsed = $finalStoreJson | ConvertFrom-Json
            $finalAccountsList = @($finalParsed.Accounts)
            $finalPlayersList = @($finalParsed.Players)
            $finalAccounts = $finalAccountsList.Count
            $finalPlayers = $finalPlayersList.Count
            $finalAccount = $finalAccountsList | Select-Object -First 1
            $finalPlayer = $finalPlayersList | Select-Object -First 1
            $expectedUser = $lockUser.ToLowerInvariant()
            Write-Host "LOCK_RETRY_ACCOUNTS=$finalAccounts"
            Write-Host "LOCK_RETRY_PLAYERS=$finalPlayers"
            Assert-ScriptCondition ($finalAccounts -eq 1) "LOCK_RETRY_ACCOUNT_COUNT" "expected=1 actual=$finalAccounts"
            Assert-ScriptCondition ($finalPlayers -eq 1) "LOCK_RETRY_PLAYER_COUNT" "expected=1 actual=$finalPlayers"
            Assert-ScriptCondition ([string]$finalAccount.NormalizedUserName -eq $expectedUser) "LOCK_RETRY_USERNAME" "expected=$expectedUser actual=$($finalAccount.NormalizedUserName)"
            Assert-ScriptCondition ([long]$finalAccount.AccountId -eq 1000001) "LOCK_RETRY_ACCOUNT_ID" "expected=1000001 actual=$($finalAccount.AccountId)"
            Assert-ScriptCondition ([long]$finalPlayer.PlayerId -eq 2000001) "LOCK_RETRY_PLAYER_ID" "expected=2000001 actual=$($finalPlayer.PlayerId)"
            Assert-ScriptCondition ([long]$finalParsed.NextAccountId -eq 1000002) "LOCK_RETRY_NEXT_ACCOUNT_ID" "expected=1000002 actual=$($finalParsed.NextAccountId)"
            Assert-ScriptCondition ([long]$finalParsed.NextPlayerId -eq 2000002) "LOCK_RETRY_NEXT_PLAYER_ID" "expected=2000002 actual=$($finalParsed.NextPlayerId)"
        }

        Write-Host "ONLINE_LIFECYCLE_SCRIPT_RESULT=$(Get-ScriptResult)"
    }

    if (-not $LoginEntryOnly) {
    $serverOut1 = Join-Path $workDir "server-primary.out.log"
    $serverErr1 = Join-Path $workDir "server-primary.err.log"
    Start-OnlineServer $StorePath $serverOut1 $serverErr1
    $primary = Invoke-TestProcess "Primary" $UserName 0 0 $StorePath
    if ($primary.ExitCode -ne 0) { $script:Failed++ }
    Stop-OnlineServer
    Flush-ServerLogSinks

    $accountMatch = [regex]::Match($primary.Output, "PRIMARY_ACCOUNT_ID=(\d+)")
    $playerMatch = [regex]::Match($primary.Output, "PRIMARY_PLAYER_ID=(\d+)")
    if (!$accountMatch.Success -or !$playerMatch.Success) {
        throw "Primary test did not print account/player ids."
    }
    $accountId = [long]$accountMatch.Groups[1].Value
    $playerId = [long]$playerMatch.Groups[1].Value

    $serverOut2 = Join-Path $workDir "server-restart.out.log"
    $serverErr2 = Join-Path $workDir "server-restart.err.log"
    Start-OnlineServer $StorePath $serverOut2 $serverErr2
    $restart = Invoke-TestProcess "RestartCheck" $UserName $accountId $playerId $StorePath
    if ($restart.ExitCode -ne 0) { $script:Failed++ }
    Stop-OnlineServer
    Flush-ServerLogSinks

    Write-Step "CLP-14 corrupt store startup guard"
    $corruptStore = "../Store/Online/identity-store-corrupt-$stamp.json"
    $resolvedCorruptStore = [System.IO.Path]::GetFullPath((Join-Path $BinDir $corruptStore))
    $script:CreatedStorePaths.Add($resolvedCorruptStore) | Out-Null
    New-Item -ItemType Directory -Force -Path ([System.IO.Path]::GetDirectoryName($resolvedCorruptStore)) | Out-Null
    [System.IO.File]::WriteAllText($resolvedCorruptStore, "{ invalid json", [System.Text.Encoding]::UTF8)
    $before = [System.IO.File]::ReadAllBytes($resolvedCorruptStore)
    $corruptOut = Join-Path $workDir "server-corrupt.out.log"
    $corruptErr = Join-Path $workDir "server-corrupt.err.log"
    $args = @("App.dll", "--AppType=Server", "--StartConfig=OnlineLocal", "--Console=0", "--OnlineStore=$corruptStore")
    Write-Host "COMMAND: dotnet $($args -join ' ')"
    $corruptProcess = Start-DotNetProcess $args $corruptOut $corruptErr
    if (-not $corruptProcess.WaitForExit(10000)) {
        Stop-Process -Id $corruptProcess.Id -Force
        Write-Host "CLP-14 | Failed | 10000ms | corrupt store server did not exit"
        $script:Failed++
    } else {
        $after = [System.IO.File]::ReadAllBytes($resolvedCorruptStore)
        $unchanged = [System.Convert]::ToBase64String($before) -eq [System.Convert]::ToBase64String($after)
        if ($corruptProcess.ExitCode -ne 0 -and $unchanged) {
            Write-Host "CLP-14 | Passed | 0ms |"
        } else {
            Write-Host "CLP-14 | Failed | 0ms | exit=$($corruptProcess.ExitCode) unchanged=$unchanged"
            $script:Failed++
        }
    }
    Flush-ServerLogSinks

    $badEncodedStore = "../Store/Online/identity-store-corrupt-encoded-$stamp.json"
    $resolvedBadEncodedStore = [System.IO.Path]::GetFullPath((Join-Path $BinDir $badEncodedStore))
    $script:CreatedStorePaths.Add($resolvedBadEncodedStore) | Out-Null
    $validButBad = @"
{
  "SchemaVersion": 1,
  "NextAccountId": 1000002,
  "NextPlayerId": 2000002,
  "Accounts": [
    {
      "AccountId": 1000001,
      "NormalizedUserName": "badstore",
      "DisplayUserName": "badstore",
      "AlgorithmVersion": 1,
      "Iterations": 600000,
      "SaltBase64": "not-base64!",
      "HashBase64": "AA==",
      "PlayerId": 2000001,
      "CreatedAtUtc": "2026-08-11T00:00:00.0000000Z"
    }
  ],
  "Players": [
    {
      "PlayerId": 2000001,
      "AccountId": 1000001,
      "DisplayName": "badstore",
      "Revision": 1,
      "CreatedAtUtc": "2026-08-11T00:00:00.0000000Z",
      "UpdatedAtUtc": "2026-08-11T00:00:00.0000000Z"
    }
  ]
}
"@
    [System.IO.File]::WriteAllText($resolvedBadEncodedStore, $validButBad, [System.Text.UTF8Encoding]::new($false))
    $beforeBadEncoded = [System.IO.File]::ReadAllBytes($resolvedBadEncodedStore)
    $badEncodedOut = Join-Path $workDir "server-corrupt-encoded.out.log"
    $badEncodedErr = Join-Path $workDir "server-corrupt-encoded.err.log"
    $args = @("App.dll", "--AppType=Server", "--StartConfig=OnlineLocal", "--Console=0", "--OnlineStore=$badEncodedStore")
    Write-Host "COMMAND: dotnet $($args -join ' ')"
    $badEncodedProcess = Start-DotNetProcess $args $badEncodedOut $badEncodedErr
    if (-not $badEncodedProcess.WaitForExit(10000)) {
        Stop-Process -Id $badEncodedProcess.Id -Force
        Write-Host "CLP-14 | Failed | 10000ms | encoded corrupt store server did not exit"
        $script:Failed++
    } else {
        $afterBadEncoded = [System.IO.File]::ReadAllBytes($resolvedBadEncodedStore)
        $unchangedBadEncoded = [System.Convert]::ToBase64String($beforeBadEncoded) -eq [System.Convert]::ToBase64String($afterBadEncoded)
        if ($badEncodedProcess.ExitCode -ne 0 -and $unchangedBadEncoded) {
            Write-Host "CLP-14-Encoded | Passed | 0ms |"
        } else {
            Write-Host "CLP-14-Encoded | Failed | 0ms | exit=$($badEncodedProcess.ExitCode) unchanged=$unchangedBadEncoded"
            $script:Failed++
        }
    }
    Flush-ServerLogSinks

    Write-Step "CLP-15 sensitive output scan"
    $scanFiles = @($serverOut1, $serverErr1, $serverOut2, $serverErr2, $corruptOut, $corruptErr, $badEncodedOut, $badEncodedErr)
    $combined = @($primary.Output, $restart.Output)
    $sensitiveHits = 0
    foreach ($file in $scanFiles) {
        if (-not (Test-Path $file)) {
            Write-Host "CLP-15-SCAN file=$file length=MISSING hits=1"
            $sensitiveHits++
            continue
        }
        $text = Get-Content -LiteralPath $file -Raw
        if ($null -eq $text) {
            $text = [string]::Empty
        }
        $hits = 0
        foreach ($marker in @("ValidPass_12345!", "SaltBase64", "HashBase64")) {
            if ($text.Contains($marker)) { $hits++ }
        }
        $length = (Get-Item -LiteralPath $file).Length
        Write-Host "CLP-15-SCAN file=$file length=$length hits=$hits"
        $sensitiveHits += $hits
        $combined += $text
    }
    $combinedText = $combined -join [Environment]::NewLine
    foreach ($marker in @("ValidPass_12345!", "SaltBase64", "HashBase64")) {
        if ($combinedText.Contains($marker)) { $sensitiveHits++ }
    }
    if ($sensitiveHits -gt 0) {
        Write-Host "CLP-15 | Failed | 0ms | sensitive hits=$sensitiveHits"
        $script:Failed++
    } else {
        Write-Host "CLP-15 | Passed | 0ms | scannedFiles=$($scanFiles.Count)"
    }

    Write-Step "Captured server output files"
    foreach ($file in Get-ChildItem -LiteralPath $workDir -File) {
        Write-Host "FILE: $($file.FullName)"
        Get-Content -LiteralPath $file.FullName -Raw | Write-Host
    }
    }
} catch {
    Write-Host "ONLINE_LIFECYCLE_SCRIPT_ERROR=$($_.Exception.Message)"
    Write-Host "ONLINE_LIFECYCLE_SCRIPT_STACK=$($_.ScriptStackTrace)"
    $script:Failed++
} finally {
    Stop-OnlineServer
    Close-ServerLogSinks
    foreach ($path in $script:CreatedStorePaths) {
        try {
            if (Test-Path -LiteralPath $path) {
                Remove-Item -LiteralPath $path -Force
                Write-Host "STORE_CLEANUP_DELETED=$path"
            }
            $parent = [System.IO.Path]::GetDirectoryName($path)
            $leaf = [System.IO.Path]::GetFileName($path)
            if (Test-Path -LiteralPath $parent) {
                Get-ChildItem -LiteralPath $parent -File -Filter "$leaf.*.tmp" -ErrorAction SilentlyContinue |
                    ForEach-Object {
                        Remove-Item -LiteralPath $_.FullName -Force
                        Write-Host "STORE_CLEANUP_DELETED=$($_.FullName)"
                    }
            }
        } catch {
            Write-Host "STORE_CLEANUP_FAILED=$path error=$($_.Exception.Message)"
            $script:Failed++
        }
    }
    foreach ($dir in $script:CreatedDirectories) {
        try {
            if (Test-Path -LiteralPath $dir) {
                Remove-Item -LiteralPath $dir -Recurse -Force
                Write-Host "WORKDIR_CLEANUP_DELETED=$dir"
            }
        } catch {
            Write-Host "WORKDIR_CLEANUP_FAILED=$dir error=$($_.Exception.Message)"
            $script:Failed++
        }
    }
}

Write-Host "ONLINE_LIFECYCLE_SCRIPT_RESULT=$(Get-ScriptResult)"
exit (Get-ScriptExitCode)
