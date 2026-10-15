param([switch]$RoutesOnly)
$ErrorActionPreference = 'Stop'
$apkTestRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('AnotherDayApkTests_' + [Guid]::NewGuid().ToString('N'))
$serverProcess = $null
try {
    New-Item -ItemType Directory -Path $apkTestRoot | Out-Null
    # 只装载函数定义，不启动实际服务器监听。
    $tokens = $null; $errors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'resource_server.ps1'), [ref]$tokens, [ref]$errors)
    foreach ($function in $ast.EndBlock.Statements | Where-Object { $_ -is [System.Management.Automation.Language.FunctionDefinitionAst] }) {
        Invoke-Expression $function.Extent.Text
    }
    function Check([bool]$condition, [string]$message) { if (-not $condition) { throw $message } }
    $apk = Join-Path $apkTestRoot 'release.apk'
    [System.IO.File]::WriteAllBytes($apk, [byte[]](1,2,3))
    Check ((Resolve-AppUpdatePath '/app-updates/android/release.apk' $apkTestRoot).Status -eq 200) 'APK route should resolve'
    foreach ($path in @('/app-updates/android/../secret.apk', '/app-updates/android/%2e%2e%2fsecret.apk', '/app-updates/android/a/b.apk', '/app-updates/android/secret.json', '/app-updates/android/a%5cb.apk')) {
        Check ((Resolve-AppUpdatePath $path $apkTestRoot).Status -eq 403) "Unsafe route accepted: $path"
    }
    Check ((Get-ContentType $apk) -eq 'application/vnd.android.package-archive') 'Wrong APK MIME type'
    if (-not $RoutesOnly) {
        $bundle = Join-Path $apkTestRoot 'Bundle'
        New-Item -ItemType Directory -Path (Join-Path $bundle 'ResourceFiles') | Out-Null
        Set-Content -LiteralPath (Join-Path $bundle 'ResourceFiles/DefaultPackage.version') -Value 'test'
        & (Join-Path $PSScriptRoot 'publish_app_update.ps1') -Apk $apk -VersionName '0.1.10' -VersionCode 2 -Bundle $bundle
        $folder = Join-Path $bundle 'AppUpdates/Android'
        $manifest = Get-Content -Raw -LiteralPath (Join-Path $folder 'app-update.json') | ConvertFrom-Json
        Check ($manifest.sizeBytes -eq 3) 'Published size wrong'
        Check ($manifest.sha256 -eq (Get-FileHash -LiteralPath $apk -Algorithm SHA256).Hash.ToLowerInvariant()) 'Published hash wrong'
        Check (Test-Path -LiteralPath (Join-Path $folder $manifest.apkFileName)) 'Published APK missing'
        $old = $manifest.apkFileName
        & (Join-Path $PSScriptRoot 'publish_app_update.ps1') -Apk $apk -VersionName '0.1.11' -VersionCode 3 -Bundle $bundle
        Check (Test-Path -LiteralPath (Join-Path $folder $old)) 'Publishing must retain old APK'
        $before = Get-Content -Raw -LiteralPath (Join-Path $folder 'app-update.json')
        [System.IO.File]::WriteAllBytes($apk, [byte[]](3,2,1))
        $rejected = $false
        try { & (Join-Path $PSScriptRoot 'publish_app_update.ps1') -Apk $apk -VersionName '0.1.11' -VersionCode 3 -Bundle $bundle }
        catch { $rejected = $true }
        Check $rejected 'Same version must not replace different bytes'
        Check ((Get-Content -Raw -LiteralPath (Join-Path $folder 'app-update.json')) -eq $before) 'Failed publish changed manifest'
        # 启动只属于本次测试的服务，以真实 HTTP 验证 GET/HEAD 与路由隔离。
        $resourceRoot = Join-Path $bundle 'ResourceFiles'
        foreach ($name in @('DefaultPackage_test.bytes', 'DefaultPackage_test.hash', 'test.bundle')) {
            Set-Content -LiteralPath (Join-Path $resourceRoot $name) -Value 'test'
        }
        $portProbe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
        $portProbe.Start(); $testPort = $portProbe.LocalEndpoint.Port; $portProbe.Stop()
        $serverScript = Join-Path $PSScriptRoot 'resource_server.ps1'
        $serverProcess = Start-Process -FilePath 'powershell.exe' -WindowStyle Hidden -PassThru -ArgumentList @(
            '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"' + $serverScript + '"'),
            '-Port', $testPort, '-Root', ('"' + $resourceRoot + '"'), '-AppUpdateRoot', ('"' + $folder + '"')
        )
        $base = "http://127.0.0.1:$testPort"
        $response = $null
        for ($attempt = 0; $attempt -lt 40; ++$attempt) {
            try { $response = Invoke-WebRequest -UseBasicParsing -Uri "$base/app-update.json" -TimeoutSec 1; break }
            catch { Start-Sleep -Milliseconds 100 }
        }
        Check ($null -ne $response -and $response.StatusCode -eq 200) 'Live manifest GET failed'
        Check ($response.Headers['Cache-Control'] -match 'no-store') 'Manifest must not be cached'
        $latest = $response.Content | ConvertFrom-Json
        $head = Invoke-WebRequest -UseBasicParsing -Method Head -Uri "$base/app-updates/android/$($latest.apkFileName)" -TimeoutSec 3
        Check ($head.StatusCode -eq 200 -and [long]$head.Headers['Content-Length'] -eq 3) 'Live APK HEAD wrong'
        $download = Join-Path $apkTestRoot 'download.apk'
        Invoke-WebRequest -UseBasicParsing -Uri "$base/app-updates/android/$($latest.apkFileName)" -OutFile $download -TimeoutSec 3
        Check ((Get-FileHash -LiteralPath $download).Hash.ToLowerInvariant() -eq $latest.sha256) 'Live downloaded bytes wrong'
        $status = 0
        try { Invoke-WebRequest -UseBasicParsing -Uri "$base/app-updates/android/secret.json" -TimeoutSec 3 | Out-Null }
        catch { $status = [int]$_.Exception.Response.StatusCode }
        Check ($status -eq 403) 'Live route exposed non APK'
    }
    Write-Output 'App update routes and publication checks passed.'
} finally {
    if ($null -ne $serverProcess -and -not $serverProcess.HasExited) {
        $serverProcess.Kill(); $serverProcess.WaitForExit(5000) | Out-Null
    }
    # 只删除本次在系统临时目录创建的唯一测试目录。
    $apkTestResolved = [IO.Path]::GetFullPath($apkTestRoot)
    $apkTestTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if ($apkTestResolved.StartsWith($apkTestTemp, [StringComparison]::OrdinalIgnoreCase) -and (Split-Path $apkTestResolved -Leaf).StartsWith('AnotherDayApkTests_')) {
        Remove-Item -LiteralPath $apkTestResolved -Recurse -Force
    }
}
