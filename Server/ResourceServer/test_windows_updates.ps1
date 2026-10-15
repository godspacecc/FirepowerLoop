param([switch]$RoutesOnly)
$ErrorActionPreference = 'Stop'
$pcTestRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('AnotherDayPcTests_' + [Guid]::NewGuid().ToString('N'))
$serverProcess = $null
try {
    New-Item -ItemType Directory -Path $pcTestRoot | Out-Null
    # 只装载函数定义，不启动实际服务器监听。
    $tokens = $null; $errors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'resource_server.ps1'), [ref]$tokens, [ref]$errors)
    foreach ($function in $ast.EndBlock.Statements | Where-Object { $_ -is [System.Management.Automation.Language.FunctionDefinitionAst] }) {
        Invoke-Expression $function.Extent.Text
    }
    function Check([bool]$condition, [string]$message) { if (-not $condition) { throw $message } }
    $installer = Join-Path $pcTestRoot 'AnotherDay-Setup-2.exe'
    [System.IO.File]::WriteAllBytes($installer, [byte[]](1,2,3))
    Check ((Resolve-WindowsAppUpdatePath '/app-updates/windows/AnotherDay-Setup-2.exe' $pcTestRoot).Status -eq 200) 'Installer route should resolve'
    foreach ($path in @('/app-updates/windows/../secret.exe', '/app-updates/windows/%2e%2e%2fsecret.exe', '/app-updates/windows/a/b.exe', '/app-updates/windows/secret.json', '/app-updates/windows/a%5cb.exe')) {
        Check ((Resolve-WindowsAppUpdatePath $path $pcTestRoot).Status -eq 403) "Unsafe route accepted: $path"
    }
    Check ((Get-ContentType $installer) -eq 'application/octet-stream') 'Wrong Installer MIME type'
    if (-not $RoutesOnly) {
        $bundle = Join-Path $pcTestRoot 'Bundle'
        New-Item -ItemType Directory -Path (Join-Path $bundle 'ResourceFiles') | Out-Null
        Set-Content -LiteralPath (Join-Path $bundle 'ResourceFiles/DefaultPackage.version') -Value 'test'
        $pcManifestPath = Join-Path $pcTestRoot 'manifest.json'
        function Write-Manifest([int]$release) {
            [ordered]@{ schemaVersion=1; productId='com.firepowerloop.game'; platform='Windows'; architecture='x64'; versionName='0.1.10'; releaseNumber=$release; installerFileName=(Split-Path $installer -Leaf); sizeBytes=(Get-Item -LiteralPath $installer).Length; sha256=(Get-FileHash -LiteralPath $installer).Hash.ToLowerInvariant() } | ConvertTo-Json | Set-Content -LiteralPath $pcManifestPath -Encoding UTF8
        }
        Write-Manifest 2
        & (Join-Path $PSScriptRoot 'publish_windows_update.ps1') -Installer $installer -Manifest $pcManifestPath -Bundle $bundle
        $folder = Join-Path $bundle 'AppUpdates/Windows'
        $manifest = Get-Content -Raw -LiteralPath (Join-Path $folder 'app-update-windows.json') | ConvertFrom-Json
        Check ($manifest.sizeBytes -eq 3) 'Published size wrong'
        Check ($manifest.sha256 -eq (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()) 'Published hash wrong'
        Check (Test-Path -LiteralPath (Join-Path $folder $manifest.installerFileName)) 'Published Installer missing'
        $old = $manifest.installerFileName
        # 旧项目的安装包不得覆盖本项目的更新入口。
        $beforeLegacy = Get-Content -Raw -LiteralPath (Join-Path $folder 'app-update-windows.json')
        $legacyManifest = (Get-Content -Raw -LiteralPath $pcManifestPath).Replace('com.firepowerloop.game', 'com.anotherday.game')
        Set-Content -LiteralPath $pcManifestPath -Value $legacyManifest -Encoding UTF8
        $legacyRejected = $false
        try { & (Join-Path $PSScriptRoot 'publish_windows_update.ps1') -Installer $installer -Manifest $pcManifestPath -Bundle $bundle }
        catch { $legacyRejected = $true }
        Check $legacyRejected 'Legacy product publication accepted'
        Check ((Get-Content -Raw -LiteralPath (Join-Path $folder 'app-update-windows.json')) -eq $beforeLegacy) 'Legacy product changed latest manifest'
        $installer = Join-Path $pcTestRoot 'AnotherDay-Setup-3.exe'
        [IO.File]::WriteAllBytes($installer, [byte[]](1,2,3))
        Write-Manifest 3
        & (Join-Path $PSScriptRoot 'publish_windows_update.ps1') -Installer $installer -Manifest $pcManifestPath -Bundle $bundle
        Check (Test-Path -LiteralPath (Join-Path $folder $old)) 'Publishing must retain old Installer'
        $before = Get-Content -Raw -LiteralPath (Join-Path $folder 'app-update-windows.json')
        [System.IO.File]::WriteAllBytes($installer, [byte[]](3,2,1))
        Write-Manifest 3
        $rejected = $false
        try { & (Join-Path $PSScriptRoot 'publish_windows_update.ps1') -Installer $installer -Manifest $pcManifestPath -Bundle $bundle }
        catch { $rejected = $true }
        Check $rejected 'Same release must not replace different bytes'
        Check ((Get-Content -Raw -LiteralPath (Join-Path $folder 'app-update-windows.json')) -eq $before) 'Failed publish changed manifest'
        Write-Manifest 1
        $rejected = $false
        try { & (Join-Path $PSScriptRoot 'publish_windows_update.ps1') -Installer $installer -Manifest $pcManifestPath -Bundle $bundle } catch { $rejected = $true }
        Check $rejected 'Downgrade publication accepted'
        Check ((Get-Content -Raw -LiteralPath (Join-Path $folder 'app-update-windows.json')) -eq $before) 'Downgrade changed latest manifest'
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
            '-Port', $testPort, '-Root', ('"' + $resourceRoot + '"'), '-WindowsAppUpdateRoot', ('"' + $folder + '"')
        )
        $base = "http://127.0.0.1:$testPort"
        $response = $null
        for ($attempt = 0; $attempt -lt 40; ++$attempt) {
            try { $response = Invoke-WebRequest -UseBasicParsing -Uri "$base/app-update-windows.json" -TimeoutSec 1; break }
            catch { Start-Sleep -Milliseconds 100 }
        }
        Check ($null -ne $response -and $response.StatusCode -eq 200) 'Live manifest GET failed'
        Check ($response.Headers['Cache-Control'] -match 'no-store') 'Manifest must not be cached'
        $latest = $response.Content | ConvertFrom-Json
        $head = Invoke-WebRequest -UseBasicParsing -Method Head -Uri "$base/app-updates/windows/$($latest.installerFileName)" -TimeoutSec 3
        Check ($head.StatusCode -eq 200 -and [long]$head.Headers['Content-Length'] -eq 3) 'Live Installer HEAD wrong'
        $download = Join-Path $pcTestRoot 'download.exe'
        Invoke-WebRequest -UseBasicParsing -Uri "$base/app-updates/windows/$($latest.installerFileName)" -OutFile $download -TimeoutSec 3
        Check ((Get-FileHash -LiteralPath $download).Hash.ToLowerInvariant() -eq $latest.sha256) 'Live downloaded bytes wrong'
        $status = 0
        try { Invoke-WebRequest -UseBasicParsing -Uri "$base/app-updates/windows/secret.json" -TimeoutSec 3 | Out-Null }
        catch { $status = [int]$_.Exception.Response.StatusCode }
        Check ($status -eq 403) 'Live route exposed non Installer'
    }
    Write-Output 'App update routes and publication checks passed.'
} finally {
    if ($null -ne $serverProcess -and -not $serverProcess.HasExited) {
        $serverProcess.Kill(); $serverProcess.WaitForExit(5000) | Out-Null
    }
    # 只删除本次在系统临时目录创建的唯一测试目录。
    $pcTestResolved = [IO.Path]::GetFullPath($pcTestRoot)
    $pcTestTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if ($pcTestResolved.StartsWith($pcTestTemp, [StringComparison]::OrdinalIgnoreCase) -and (Split-Path $pcTestResolved -Leaf).StartsWith('AnotherDayPcTests_')) {
        Remove-Item -LiteralPath $pcTestResolved -Recurse -Force
    }
}
