param(
    [Parameter(Mandatory = $true)]
    [string]$GameServerRoot
)

$ErrorActionPreference = "Stop"
$script:publishLock = $null
$script:exitCode = 1
$script:rebuildStarted = $false

function Convert-ToFullPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    return [System.IO.Path]::GetFullPath($Path)
}

function Test-Net8SdkInstalled {
    $sdks = & dotnet --list-sdks
    if ($LASTEXITCODE -ne 0) {
        throw "执行 dotnet --list-sdks 失败，ExitCode=$LASTEXITCODE。"
    }
    if (-not ($sdks | Where-Object { $_ -match "^\s*8\." })) {
        throw "未找到已安装的 .NET 8 SDK。"
    }
}

function Test-SelectedNet8Sdk {
    param([Parameter(Mandatory = $true)][string]$Root)

    Push-Location -LiteralPath $Root
    try {
        $version = & dotnet --version
        if ($LASTEXITCODE -ne 0) {
            throw "在 GameServerRoot 下执行 dotnet --version 失败，ExitCode=$LASTEXITCODE。"
        }
        if ($version -notmatch "^\s*8\.") {
            throw "当前项目实际选择的 SDK 不是 .NET 8：$version。"
        }
    }
    finally {
        Pop-Location
    }
}

function Resolve-AndValidateContext {
    param([Parameter(Mandatory = $true)][string]$Root)

    $normalizedRoot = Convert-ToFullPath $Root
    $publishRoot = Convert-ToFullPath (Join-Path $normalizedRoot "Publish\win-x64")
    $context = [PSCustomObject]@{
        GameServerRoot = $normalizedRoot
        ProjectPath = Convert-ToFullPath (Join-Path $normalizedRoot "DotNet\App\DotNet.App.csproj")
        HotfixProjectPath = Convert-ToFullPath (Join-Path $normalizedRoot "DotNet\Hotfix\DotNet.Hotfix.csproj")
        GlobalJson = Convert-ToFullPath (Join-Path $normalizedRoot "global.json")
        SourceConfig = Convert-ToFullPath (Join-Path $normalizedRoot "Config")
        NLogConfig = Convert-ToFullPath (Join-Path $normalizedRoot "Config\NLog\NLog.config")
        LubanConfig = Convert-ToFullPath (Join-Path $normalizedRoot "Config\Luban")
        SourcePublishBat = Convert-ToFullPath (Join-Path $normalizedRoot "Publish-win-x64.bat")
        SourceStartBat = Convert-ToFullPath (Join-Path $normalizedRoot "Tools\start-published-online-server.bat")
        SourceLauncher = Convert-ToFullPath (Join-Path $normalizedRoot "Tools\start-local-online-server.ps1")
        SourceDiagnosticStats = Convert-ToFullPath (Join-Path $normalizedRoot "Tools\get-diagnostic-stats.ps1")
        SourceDeploymentGuide = Convert-ToFullPath (Join-Path $normalizedRoot "局域网部署说明.md")
        PublishRoot = $publishRoot
        Bin = Convert-ToFullPath (Join-Path $publishRoot "Bin")
        Config = Convert-ToFullPath (Join-Path $publishRoot "Config")
        Tools = Convert-ToFullPath (Join-Path $publishRoot "Tools")
        PublishedStartBat = Convert-ToFullPath (Join-Path $publishRoot "启动联机服务器.bat")
        PublishedLauncher = Convert-ToFullPath (Join-Path $publishRoot "Tools\start-local-online-server.ps1")
        PublishedDiagnosticStats = Convert-ToFullPath (Join-Path $publishRoot "Tools\get-diagnostic-stats.ps1")
        PublishedDeploymentGuide = Convert-ToFullPath (Join-Path $publishRoot "局域网部署说明.md")
        LockPath = Convert-ToFullPath (Join-Path $normalizedRoot "Publish\win-x64.publish.lock")
    }

    if ([System.Environment]::OSVersion.Platform -ne [System.PlatformID]::Win32NT) {
        throw "Windows 联机服务器发布工具只能在 Windows 上运行。"
    }
    if (-not (Test-Path -LiteralPath $context.GameServerRoot -PathType Container)) {
        throw "GameServerRoot 不存在：$($context.GameServerRoot)"
    }
    if ($null -eq (Get-Command "dotnet" -ErrorAction SilentlyContinue)) {
        throw "当前环境未找到 dotnet 命令。"
    }

    Test-Net8SdkInstalled
    Test-SelectedNet8Sdk -Root $context.GameServerRoot

    $requiredFiles = @(
        @($context.ProjectPath, "服务端入口项目"),
        @($context.HotfixProjectPath, "Hotfix 项目"),
        @($context.GlobalJson, "项目级 SDK 配置 global.json"),
        @($context.NLogConfig, "NLog 配置"),
        @($context.SourcePublishBat, "源码目录发布 BAT"),
        @($context.SourceStartBat, "发布包启动 BAT 模板"),
        @($context.SourceLauncher, "PowerShell 启动器"),
        @($context.SourceDiagnosticStats, "诊断统计脚本"),
        @($context.SourceDeploymentGuide, "局域网部署说明")
    )
    foreach ($item in $requiredFiles) {
        if (-not (Test-Path -LiteralPath $item[0] -PathType Leaf)) {
            throw "$($item[1])不存在：$($item[0])"
        }
    }
    if (-not (Test-Path -LiteralPath $context.LubanConfig -PathType Container)) {
        throw "Luban 配置目录不存在：$($context.LubanConfig)"
    }

    return $context
}

function Assert-ChildPath {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$ExpectedParent
    )

    $normalizedPath = Convert-ToFullPath $Path
    $normalizedParent = (Convert-ToFullPath $ExpectedParent).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $prefix = $normalizedParent + [System.IO.Path]::DirectorySeparatorChar

    if ($normalizedPath.Equals($normalizedParent, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "拒绝修改发布根目录本身：$normalizedPath"
    }
    if (-not $normalizedPath.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "拒绝修改发布根目录之外的路径：$normalizedPath"
    }

    return $normalizedPath
}

function Acquire-PublishLock {
    param([Parameter(Mandatory = $true)][string]$LockPath)

    $lockDirectory = Split-Path -Parent $LockPath
    New-Item -ItemType Directory -Force -Path $lockDirectory | Out-Null

    try {
        return [System.IO.FileStream]::new(
            $LockPath,
            [System.IO.FileMode]::OpenOrCreate,
            [System.IO.FileAccess]::ReadWrite,
            [System.IO.FileShare]::None)
    }
    catch [System.IO.IOException] {
        throw "已有 win-x64 发布正在进行，请等待现有发布窗口结束。"
    }
}

function Release-PublishLock {
    if ($null -ne $script:publishLock) {
        $script:publishLock.Dispose()
        $script:publishLock = $null
    }
}

function Remove-PublishOutputs {
    param([Parameter(Mandatory = $true)]$Context)

    # Store 是发布根的运行数据，重建只清理可重建的程序与工具目录。
    foreach ($directory in @($Context.Bin, $Context.Config, $Context.Tools)) {
        $safeDirectory = Assert-ChildPath -Path $directory -ExpectedParent $Context.PublishRoot
        if (Test-Path -LiteralPath $safeDirectory -PathType Container) {
            Remove-Item -LiteralPath $safeDirectory -Recurse -Force
        }
    }

    $safeStartBat = Assert-ChildPath -Path $Context.PublishedStartBat -ExpectedParent $Context.PublishRoot
    if (Test-Path -LiteralPath $safeStartBat -PathType Leaf) {
        Remove-Item -LiteralPath $safeStartBat -Force
    }

    $safeDeploymentGuide = Assert-ChildPath -Path $Context.PublishedDeploymentGuide -ExpectedParent $Context.PublishRoot
    if (Test-Path -LiteralPath $safeDeploymentGuide -PathType Leaf) {
        Remove-Item -LiteralPath $safeDeploymentGuide -Force
    }
}

function Invoke-WindowsPublish {
    param([Parameter(Mandatory = $true)]$Context)

    New-Item -ItemType Directory -Force -Path $Context.Bin | Out-Null

    $appArguments = @(
        "publish",
        $Context.ProjectPath,
        "--configuration", "Release",
        "--self-contained", "false",
        "--output", $Context.Bin,
        "-p:NuGetAudit=false"
    )
    $hotfixArguments = @(
        "publish",
        $Context.HotfixProjectPath,
        "--configuration", "Release",
        "--self-contained", "false",
        "--output", $Context.Bin,
        "-p:NuGetAudit=false"
    )

    Push-Location -LiteralPath $Context.GameServerRoot
    try {
        & dotnet @appArguments
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet publish App 失败，ExitCode=$LASTEXITCODE。"
        }

        & dotnet @hotfixArguments
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet publish Hotfix 失败，ExitCode=$LASTEXITCODE。"
        }
    }
    finally {
        Pop-Location
    }
}

function Copy-PublishConfig {
    param([Parameter(Mandatory = $true)]$Context)

    $safeConfig = Assert-ChildPath -Path $Context.Config -ExpectedParent $Context.PublishRoot
    Copy-Item -LiteralPath $Context.SourceConfig -Destination $safeConfig -Recurse -Force
}

function Copy-DeploymentLaunchers {
    param([Parameter(Mandatory = $true)]$Context)

    $safeTools = Assert-ChildPath -Path $Context.Tools -ExpectedParent $Context.PublishRoot
    New-Item -ItemType Directory -Force -Path $safeTools | Out-Null
    Copy-Item -LiteralPath $Context.SourceLauncher -Destination $Context.PublishedLauncher -Force
    Copy-Item -LiteralPath $Context.SourceDiagnosticStats -Destination $Context.PublishedDiagnosticStats -Force
    Copy-Item -LiteralPath $Context.SourceStartBat -Destination $Context.PublishedStartBat -Force
    Copy-Item -LiteralPath $Context.SourceDeploymentGuide -Destination $Context.PublishedDeploymentGuide -Force
}

function Validate-PublishOutput {
    param([Parameter(Mandatory = $true)]$Context)

    $requiredFiles = @(
        (Join-Path $Context.Bin "App.exe"),
        (Join-Path $Context.Bin "App.dll"),
        (Join-Path $Context.Bin "App.runtimeconfig.json"),
        (Join-Path $Context.Bin "App.deps.json"),
        (Join-Path $Context.Bin "Hotfix.dll"),
        (Join-Path $Context.Bin "Hotfix.pdb"),
        (Join-Path $Context.Bin "Hotfix.deps.json"),
        (Join-Path $Context.Config "NLog\NLog.config"),
        $Context.PublishedLauncher,
        $Context.PublishedDiagnosticStats,
        $Context.PublishedStartBat,
        $Context.PublishedDeploymentGuide
    )
    foreach ($file in $requiredFiles) {
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
            throw "发布产物缺失：$file"
        }
    }

    $lubanOutput = Join-Path $Context.Config "Luban"
    if (-not (Test-Path -LiteralPath $lubanOutput -PathType Container)) {
        throw "发布产物目录缺失：$lubanOutput"
    }
}

try {
    Write-Host "正在准备 Windows 联机服务器发布..."
    $context = Resolve-AndValidateContext -Root $GameServerRoot
    $script:publishLock = Acquire-PublishLock -LockPath $context.LockPath

    Remove-PublishOutputs -Context $context
    $script:rebuildStarted = $true

    Invoke-WindowsPublish -Context $context
    Copy-PublishConfig -Context $context
    Copy-DeploymentLaunchers -Context $context
    Validate-PublishOutput -Context $context

    Write-Host ""
    Write-Host "发布成功。"
    Write-Host "目标目录名：win-x64"
    Write-Host "运行环境要求：目标设备需安装兼容的 .NET 8 Runtime。"
    Write-Host "完整发布目录：$($context.PublishRoot)"
    Write-Host "部署后请双击 启动联机服务器.bat 启动；不会自动运行 App.exe 或 dotnet App.dll。"
    $script:exitCode = 0
}
catch {
    Write-Host ""
    Write-Host "发布失败。"
    Write-Host $_.Exception.Message
    if ($script:rebuildStarted -and $null -ne $context) {
        try {
            Remove-PublishOutputs -Context $context
        }
        catch {
            Write-Host "清理本次不完整发布失败：$($_.Exception.Message)"
        }
    }
    $script:exitCode = 1
}
finally {
    Release-PublishLock
    Write-Host ""
    if ($script:exitCode -eq 0) {
        Read-Host "按 Enter 关闭发布窗口"
    }
    else {
        Write-Host "ExitCode=$script:exitCode"
        Read-Host "按 Enter 关闭失败的发布窗口"
    }
    exit $script:exitCode
}
