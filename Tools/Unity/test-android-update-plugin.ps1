param([string]$ApkPath)
$ErrorActionPreference = 'Stop'

# 不执行打包：检查 Gradle 默认源码集，传入已有 APK 时检查最终 DEX 类是否齐全。
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$plugin = Join-Path $repository 'Client/Assets/Plugins/Android/AnotherDayUpdate.androidlib'
[xml]$manifest = Get-Content -LiteralPath (Join-Path $plugin 'AndroidManifest.xml')
$provider = $manifest.manifest.application.provider.GetAttribute('name', 'http://schemas.android.com/apk/res/android')
if ($provider -ne 'com.anotherday.update.ApkProvider') { throw '更新提供器声明不符合约定。' }
$classes = @($provider, 'com.anotherday.update.ApkInstaller')
foreach ($name in $classes) {
    $source = Join-Path $plugin ('src/main/java/' + $name.Replace('.', '/') + '.java')
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Java 类未进入 Gradle 默认源码集：$name"
    }
}
Write-Output 'Android update plugin source-set checks passed.'

# 重启 Activity 也必须进入默认源码集；清单声明存在不能证明 Java 类已编译进 APK。
$restartPlugin = Join-Path $repository 'Client/Assets/Plugins/Android/AnotherDayRestart.androidlib'
[xml]$restartManifest = Get-Content -LiteralPath (Join-Path $restartPlugin 'AndroidManifest.xml')
$restartActivity = $restartManifest.manifest.application.activity
$restartClass = $restartActivity.GetAttribute('name', 'http://schemas.android.com/apk/res/android')
if ($restartClass -ne 'com.anotherday.restart.ResourceRestartActivity' -or
    $restartActivity.GetAttribute('process', 'http://schemas.android.com/apk/res/android') -ne ':resource_restart' -or
    $restartActivity.GetAttribute('exported', 'http://schemas.android.com/apk/res/android') -ne 'false') {
    throw '重启 Activity 声明不符合独立私有进程约定。'
}
$restartSource = Join-Path $restartPlugin ('src/main/java/' + $restartClass.Replace('.', '/') + '.java')
if (-not (Test-Path -LiteralPath $restartSource -PathType Leaf)) {
    throw "Java 类未进入 Gradle 默认源码集：$restartClass"
}
$classes += $restartClass
Write-Output 'Android restart plugin source-set checks passed.'

# 新宿主使用异步交接 API，仅验证类存在不能发现 APK 内仍携带旧版同步插件。
$bridgeMethods = @('beginInstall', 'getInstallResult', 'cancelInstall')
$installerSource = Get-Content -Raw -LiteralPath (Join-Path $plugin 'src/main/java/com/anotherday/update/ApkInstaller.java')
foreach ($method in $bridgeMethods) {
    if ($installerSource -notmatch ('public\s+static\s+(void|String)\s+' + $method + '\s*\(')) {
        throw "安装插件缺少异步接口：$method"
    }
}
Write-Output 'Android update asynchronous bridge source checks passed.'

if ($ApkPath) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($ApkPath))
    try {
        $found = @{}
        $foundMethods = @{}
        foreach ($entry in $archive.Entries) {
            if ($entry.FullName -notmatch '^classes[0-9]*\.dex$') { continue }
            $reader = [IO.BinaryReader]::new($entry.Open())
            try {
                $content = [Text.Encoding]::ASCII.GetString($reader.ReadBytes([int]$entry.Length))
                foreach ($name in $classes) {
                    if ($content.Contains('L' + $name.Replace('.', '/') + ';')) { $found[$name] = $true }
                }
                foreach ($method in $bridgeMethods) {
                    if ($content.Contains($method)) { $foundMethods[$method] = $true }
                }
            } finally { $reader.Dispose() }
        }
        foreach ($name in $classes) {
            if (-not $found.ContainsKey($name)) { throw "APK 缺少更新或重启插件类：$name" }
        }
        foreach ($method in $bridgeMethods) {
            if (-not $foundMethods.ContainsKey($method)) { throw "APK 缺少异步安装接口：$method" }
        }
        Write-Output 'Android update plugin APK DEX checks passed.'
    } finally { $archive.Dispose() }
}
