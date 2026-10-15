param([string]$Address = "127.0.0.1:31011", [int]$StartupTimeoutSeconds = 20)
$ErrorActionPreference = "Stop"
$server = $null
$failed = 0
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
$bin = (Resolve-Path (Join-Path $PSScriptRoot "..\Bin")).Path
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$work = Join-Path ([IO.Path]::GetTempPath()) "AnotherDayServerTestCmd-$stamp"
$store = "../Store/Online/identity-store-server-test-cmd-$stamp.json"
$storeFull = [IO.Path]::GetFullPath((Join-Path $bin $store))
New-Item -ItemType Directory -Force -Path $work | Out-Null
function Wait-Port {
    $hostName,$portText=$Address.Split(':'); $deadline=(Get-Date).AddSeconds($StartupTimeoutSeconds)
    while((Get-Date) -lt $deadline){ try{$c=[Net.Sockets.TcpClient]::new(); if($c.ConnectAsync($hostName,[int]$portText).Wait(300) -and $c.Connected){$c.Dispose();return};$c.Dispose()}catch{}; Start-Sleep -Milliseconds 250; if($server -and $server.HasExited){throw "server exited: $($server.ExitCode)"} }
    throw "server startup timeout"
}
try {
    $dotnet=(Get-Command dotnet).Source
    $out=Join-Path $work 'server.out.log'; $err=Join-Path $work 'server.err.log'
    $server=Start-Process -FilePath $dotnet -ArgumentList @('App.dll','--AppType=Server','--StartConfig=OnlineLocal','--Console=0',"--OnlineStore=$store") -WorkingDirectory $bin -RedirectStandardOutput $out -RedirectStandardError $err -PassThru -WindowStyle Hidden
    Wait-Port
    Push-Location $bin
    try { & $dotnet App.dll --AppType=OnlineTest --Console=0 --OnlineTestMode=ServerTestCmd --OnlineTestAddress=$Address --OnlineStore=$store 2>&1 | ForEach-Object { Write-Host $_ }; if($LASTEXITCODE -ne 0){$failed++} }
    finally { Pop-Location }
} catch { Write-Host "SERVER_TEST_CMD_SCRIPT_ERROR=$($_.Exception.Message)"; $failed++ }
finally {
    if($server -and -not $server.HasExited){Stop-Process -Id $server.Id -Force; $server.WaitForExit(5000)|Out-Null}
    if(Test-Path $storeFull){Remove-Item -LiteralPath $storeFull -Force}
    Get-ChildItem -LiteralPath $work -File -ErrorAction SilentlyContinue | ForEach-Object { Write-Host "FILE=$($_.FullName)"; Get-Content -Raw -LiteralPath $_.FullName | Write-Host }
    if(Test-Path $work){Remove-Item -LiteralPath $work -Recurse -Force}
}
Write-Host "SERVER_TEST_CMD_SCRIPT_RESULT=$(if($failed -eq 0){'Passed'}else{'Failed'})"
exit $(if($failed -eq 0){0}else{1})
