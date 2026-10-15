param(
    [string]$Address = "127.0.0.1:31011",
    [int]$StartupTimeoutSeconds = 20,
    [ValidateSet("", "WorldMaps", "WorldInventory", "WorldProjectiles")]
    [string]$Mode = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$server = $null
$sinks = @()
$failed = 0
$resolvedStores = @()
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)

function Stop-Server {
    if ($script:server -and -not $script:server.HasExited) {
        Stop-Process -Id $script:server.Id -Force
        $script:server.WaitForExit(5000) | Out-Null
    }
    $script:server = $null
}

function Close-Sinks {
    foreach ($sink in $script:sinks) {
        try {
            if (-not $sink.Process.HasExited) { $sink.Process.WaitForExit(5000) | Out-Null }
            Write-Host $sink.Stdout.Result
            Write-Host $sink.Stderr.Result
        } catch {}
    }
    $script:sinks = @()
}

function Start-ProcessCaptured([string[]]$arguments) {
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = (Get-Command dotnet).Source
    $psi.WorkingDirectory = $BinDir
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.Arguments = (($arguments | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -join ' ')
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $psi
    $process.Start() | Out-Null
    $script:sinks += [pscustomobject]@{ Process=$process; Stdout=$process.StandardOutput.ReadToEndAsync(); Stderr=$process.StandardError.ReadToEndAsync() }
    return $process
}

function Wait-Port {
    $hostName, $portText = $Address.Split(':')
    $deadline = (Get-Date).AddSeconds($StartupTimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $client = [System.Net.Sockets.TcpClient]::new()
            $connected = $client.ConnectAsync($hostName, [int]$portText).Wait(300) -and $client.Connected
            $client.Dispose()
            if ($connected) { return }
        } catch {}
        if ($script:server -and $script:server.HasExited) { throw "server exited early: $($script:server.ExitCode)" }
        Start-Sleep -Milliseconds 200
    }
    throw "timed out waiting for $Address"
}

function Start-Server([int]$failLeaveCount, [bool]$combat = $false, [int]$corpseLifetime = 0, [int]$profileFailSaveCount = 0, [int]$dropCommitResponseCount = 0, [string]$storeSuffix = "default") {
    $script:StorePath = "../Store/Online/identity-store-online-world-$stamp-$storeSuffix.json"
    $script:resolvedStores += [System.IO.Path]::GetFullPath((Join-Path $BinDir $script:StorePath))
    $args = @("App.dll", "--AppType=Server", "--StartConfig=OnlineLocal", "--Console=0", "--OnlineStore=$StorePath", "--OnlinePlayerGraceMilliseconds=1200", "--OnlineWorldFailLeaveCount=$failLeaveCount")
    $args += @("--OnlineWorldCorpseLifetimeMillisecondsOverride=$corpseLifetime", "--OnlineProfileFailSaveCount=$profileFailSaveCount", "--OnlineWorldDropLeaveCommitResponseCount=$dropCommitResponseCount")
    if ($combat) { $args += @("--OnlineWorldTickMilliseconds=20", "--OnlineWorldPlayerRespawnMilliseconds=200", "--OnlineWorldNavigationHeartbeatMilliseconds=50", "--OnlineWorldNavigationLeaseTimeoutMilliseconds=200") }
    Write-Host "SERVER_COMMAND=dotnet $($args -join ' ')"
    $script:server = Start-ProcessCaptured $args
    Wait-Port
}

function Run-Test([string]$mode, [string]$testUser = "") {
    $args = @("App.dll", "--AppType=OnlineTest", "--Console=0", "--OnlineTestMode=$mode", "--OnlineTestAddress=$Address", "--OnlineStore=$StorePath", "--OnlinePlayerGraceMilliseconds=1200")
    if ($testUser) { $args += "--OnlineTestUser=$testUser" }
    Write-Host "TEST_COMMAND=dotnet $($args -join ' ')"
    Push-Location $BinDir
    try {
        & dotnet @args 2>&1 | ForEach-Object { Write-Host $_.ToString() }
        return $LASTEXITCODE
    } finally { Pop-Location }
}

function Convert-StoreToVersionOne {
    $resolved = [System.IO.Path]::GetFullPath((Join-Path $BinDir $StorePath))
    $snapshot = [System.IO.File]::ReadAllText($resolved, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    $snapshot.SchemaVersion = 1
    foreach ($player in $snapshot.Players) {
        $player.PSObject.Properties.Remove("HasReceivedOnlineStarterSupplies")
        $player.PSObject.Properties.Remove("OnlineInventory")
    }
    $json = $snapshot | ConvertTo-Json -Depth 20
    [System.IO.File]::WriteAllText($resolved, $json, [System.Text.UTF8Encoding]::new($false))
    Write-Host "STORE_MIGRATED_TO_VERSION_ONE=$resolved"
}

try {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
    $BinDir = (Resolve-Path (Join-Path $PSScriptRoot "..\Bin")).Path
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $StorePath = ""

    if ($Mode -eq "WorldProjectiles") {
        Start-Server 0 $true
        if ((Run-Test "WorldProjectiles") -ne 0) { $failed++ }
        Stop-Server
        Close-Sinks
    } elseif ($Mode -eq "WorldInventory") {
        Start-Server 0 $true 0 0 0 "normal"
        if ((Run-Test "WorldCombat" "world-inventory-mode") -ne 0) { $failed++ }
        Stop-Server
        Close-Sinks
        Start-Server 0 $true 0 0 0 "combat-regression"
        if ((Run-Test "WorldCombat") -ne 0) { $failed++ }
        Stop-Server
        Close-Sinks
        Start-Server 0 $true 0 0 0 "migration"
        if ((Run-Test "WorldCombat" "world-inventory-migration-prepare") -ne 0) { $failed++ }
        Stop-Server
        Close-Sinks
        Convert-StoreToVersionOne
        Start-Server 0 $true 0 0 0 "migration"
        if ((Run-Test "WorldCombat" "world-inventory-migration") -ne 0) { $failed++ }
        Stop-Server
        Close-Sinks
        Start-Server 0 $true 0 0 0 "migration"
        if ((Run-Test "WorldCombat" "world-inventory-migration-restart") -ne 0) { $failed++ }
        Stop-Server
        Close-Sinks
        Start-Server 0 $true 0 0 0 "leave-failure"
        if ((Run-Test "WorldCombat" "world-inventory-leave-prepare") -ne 0) { $failed++ }
        Stop-Server
        Close-Sinks
        Start-Server 0 $true 0 1 0 "leave-failure"
        if ((Run-Test "WorldCombat" "world-inventory-leave-failure") -ne 0) { $failed++ }
        Stop-Server
        Close-Sinks
        Start-Server 0 $true 0 0 0 "grace-failure"
        if ((Run-Test "WorldCombat" "world-inventory-grace-prepare") -ne 0) { $failed++ }
        Stop-Server
        Close-Sinks
        Start-Server 0 $true 0 1 0 "grace-failure"
        if ((Run-Test "WorldCombat" "world-inventory-grace-save-failure") -ne 0) { $failed++ }
        Stop-Server
        Close-Sinks
        Start-Server 0 $true 250 0 0 "expiry"
        if ((Run-Test "WorldCombat" "world-inventory-expiry") -ne 0) { $failed++ }
        Stop-Server
        Close-Sinks
        Start-Server 0 $true 0 1 0 "profile-failure"
        if ((Run-Test "WorldCombat" "world-inventory-save-failure") -ne 0) { $failed++ }
        Stop-Server
        Close-Sinks
        Start-Server 0 $true 0 0 1 "commit-loss"
        if ((Run-Test "WorldCombat" "world-inventory-commit-loss") -ne 0) { $failed++ }
        Stop-Server
        Close-Sinks
    } elseif ($Mode -eq "WorldMaps") {
        Start-Server 0 $true
        if ((Run-Test "WorldMaps") -ne 0) { $failed++ }
        Stop-Server
        Close-Sinks
    } else {
        Start-Server 0
        if ((Run-Test "World") -ne 0) { $failed++ }
        Stop-Server
        Close-Sinks

        Start-Server 1
        if ((Run-Test "WorldRetry") -ne 0) { $failed++ }
        Stop-Server
        Close-Sinks

        Start-Server 0 $true
        if ((Run-Test "WorldCombat") -ne 0) { $failed++ }
        if ((Run-Test "WorldMaps") -ne 0) { $failed++ }
        Stop-Server
        Close-Sinks
    }
} catch {
    Write-Host "ONLINE_WORLD_SCRIPT_ERROR=$($_.Exception.Message)"
    $failed++
} finally {
    Stop-Server
    Close-Sinks
    foreach ($resolvedStore in $resolvedStores | Select-Object -Unique) {
      if ($resolvedStore -and (Test-Path -LiteralPath $resolvedStore)) {
        Remove-Item -LiteralPath $resolvedStore -Force
        Write-Host "STORE_CLEANUP_DELETED=$resolvedStore"
      }
    }
}

Write-Host "ONLINE_WORLD_SCRIPT_RESULT=$(if ($failed -eq 0) { 'Passed' } else { 'Failed' })"
exit $(if ($failed -eq 0) { 0 } else { 1 })
