param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if ([string]::IsNullOrWhiteSpace($env:FIREPOWERLOOP_TEST_MONGODB_URI)) { throw '需要独立测试Mongo的 FIREPOWERLOOP_TEST_MONGODB_URI，不使用生产配置。' }
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,31011)
try { $listener.Start() } finally { $listener.Stop() }
$batch = Join-Path $repo ('Backup/Artifacts/AccountFoundation-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
$runtime = Join-Path $batch 'runtime'
New-Item -ItemType Directory -Force -Path $runtime | Out-Null
$database = 'firepowerloop_tests_native_' + [Guid]::NewGuid().ToString('N')
$fixtureProcess = $null
$oldUri = $env:FIREPOWERLOOP_MONGODB_URI
$oldDatabase = $env:FIREPOWERLOOP_MONGODB_DATABASE
# 专用服务进程只接收测试数据库；凭据不写入证据文件。
function Start-Fixture([string]$label) {
    $script:fixtureProcess = Start-Process dotnet -ArgumentList @('App.dll','--AppType=Server','--StartConfig=OnlineLocal','--Console=0','--OnlineLoopbackOnly=true','--OnlineStore=../missing-legacy.json','--OnlineArcadeStore=../arcade-fixture.json','--DiagnosticStoreRoot=../diagnostics') -WorkingDirectory (Join-Path $runtime 'Bin') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $batch "$label.stdout.log") -RedirectStandardError (Join-Path $batch "$label.stderr.log")
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    do {
        if ($script:fixtureProcess.HasExited) { throw '测试服务启动失败，请查看本批日志。' }
        $probe = [Net.Sockets.TcpClient]::new()
        try { $probe.Connect('127.0.0.1',31011); return } catch { Start-Sleep -Milliseconds 200 } finally { $probe.Dispose() }
    } while ([DateTime]::UtcNow -lt $deadline)
    throw '测试服务未在30秒内就绪。'
}
function Stop-Fixture {
    if ($null -ne $script:fixtureProcess -and -not $script:fixtureProcess.HasExited) {
        Stop-Process -Id $script:fixtureProcess.Id
        $script:fixtureProcess.WaitForExit(10000) | Out-Null
    }
    $script:fixtureProcess = $null
}
$checksPassed = $false
'Incomplete' | Set-Content (Join-Path $batch 'result.txt')
Push-Location $repo
try {
    if (-not $SkipBuild) {
        foreach ($project in @('Server/GameServer/DotNet/App/DotNet.App.csproj','Server/GameServer/DotNet/Hotfix/DotNet.Hotfix.csproj','Tools/NetworkFoundationChecks','Tools/AccountFoundationChecks')) {
            & dotnet build $project -c Release --nologo >> (Join-Path $batch 'build.log') 2>&1
            if ($LASTEXITCODE -ne 0) { throw "编译失败：$project" }
        }
    }
    Copy-Item -LiteralPath 'Server/GameServer/Bin' -Destination $runtime -Recurse
    Copy-Item -LiteralPath 'Server/GameServer/Config' -Destination $runtime -Recurse
    $env:FIREPOWERLOOP_MONGODB_URI = $env:FIREPOWERLOOP_TEST_MONGODB_URI
    $env:FIREPOWERLOOP_MONGODB_DATABASE = $database
    & dotnet run --project Tools/AccountFoundationChecks -c Release --no-build > (Join-Path $batch 'mongo.log') 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'Mongo账号回归失败' }
    Start-Fixture 'first'
    $marker = Join-Path $batch 'identity.json'
    & dotnet run --project Tools/NetworkFoundationChecks -c Release --no-build -- --native 127.0.0.1:31011 $marker > (Join-Path $batch 'native.log') 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'ET真实账号链路失败' }
    Stop-Fixture
    Start-Fixture 'restart'
    & dotnet run --project Tools/NetworkFoundationChecks -c Release --no-build -- --native 127.0.0.1:31011 $marker --resume > (Join-Path $batch 'restart.log') 2>&1
    if ($LASTEXITCODE -ne 0) { throw '重启持久身份回归失败' }
    $checksPassed = $true
} finally {
    Stop-Fixture
    $env:FIREPOWERLOOP_MONGODB_URI = $oldUri
    $env:FIREPOWERLOOP_MONGODB_DATABASE = $oldDatabase
    & dotnet run --project Tools/AccountFoundationChecks -c Release --no-build -- --drop-test-database $database > (Join-Path $batch 'cleanup.log') 2>&1
    if ($LASTEXITCODE -ne 0) { Write-Error '隔离数据库清理失败，见cleanup.log；保留本批数据库名以便恢复。' }
    Pop-Location
    if ($checksPassed -and $LASTEXITCODE -eq 0) {
        'Passed: Mongo账户策略、真实ET链路及重启持久身份；未包含诊断HTTP、Unity、房间玩法或Player验收。' | Set-Content (Join-Path $batch 'result.txt')
        Write-Output "账号基础检查通过：$batch"
    }
}
