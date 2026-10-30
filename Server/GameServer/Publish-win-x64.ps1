param(
    [Parameter(Mandatory = $true)]
    [string]$GameServerRoot
)

$ErrorActionPreference = "Stop"
$script:publishLock = $null
$script:exitCode = 1
# 只替换可重建产物，运行数据和其他文件始终留在正式目录。
$script:managedNames = @('Bin', 'Config', 'Tools', '启动联机服务器.bat', '局域网部署说明.md')
$script:stageContext = $null
$script:replacementStarted = $false

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

# 拒绝经目录联接或符号链接修改其他位置；移动、递归清理前均调用。
function Assert-PlainTree {
    param([Parameter(Mandatory = $true)][string]$Path)
    $cursor = Convert-ToFullPath $Path
    while ($cursor) {
        if (Test-Path -LiteralPath $cursor) {
            $item = Get-Item -LiteralPath $cursor -Force
            if ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
                throw "拒绝修改包含链接的路径：$cursor"
            }
        }
        $cursor = Split-Path -Parent $cursor
    }
    if (Test-Path -LiteralPath $Path -PathType Container) {
        foreach ($item in Get-ChildItem -LiteralPath $Path -Force -Recurse) {
            if ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
                throw "拒绝修改包含链接的目录：$($item.FullName)"
            }
        }
    }
}

# 复用源配置，仅把产物路径映射到本次独立临时目录。
function New-OutputContext {
    param($Context, [string]$OutputRoot)
    $copy = $Context.PSObject.Copy()
    $copy.PublishRoot = Convert-ToFullPath $OutputRoot
    $copy.Bin = Join-Path $copy.PublishRoot 'Bin'
    $copy.Config = Join-Path $copy.PublishRoot 'Config'
    $copy.Tools = Join-Path $copy.PublishRoot 'Tools'
    $copy.PublishedStartBat = Join-Path $copy.PublishRoot '启动联机服务器.bat'
    $copy.PublishedLauncher = Join-Path $copy.Tools 'start-local-online-server.ps1'
    $copy.PublishedDiagnosticStats = Join-Path $copy.Tools 'get-diagnostic-stats.ps1'
    $copy.PublishedDeploymentGuide = Join-Path $copy.PublishRoot '局域网部署说明.md'
    return $copy
}

# 记录受管产物的结构与文件哈希，也能核对不完整旧包的原貌。
function Get-OutputSnapshot {
    param([string]$Root)
    $records = [System.Collections.Generic.List[string]]::new()
    foreach ($name in $script:managedNames) {
        $path = Assert-ChildPath -Path (Join-Path $Root $name) -ExpectedParent $Root
        Assert-PlainTree -Path $path
        if (-not (Test-Path -LiteralPath $path)) {
            $records.Add("M|$name")
            continue
        }
        $items = @(Get-Item -LiteralPath $path -Force)
        if ($items[0].PSIsContainer) {
            $items += @(Get-ChildItem -LiteralPath $path -Force -Recurse)
        }
        foreach ($item in $items) {
            $relative = $item.FullName.Substring($Root.TrimEnd('\').Length + 1)
            if ($item.PSIsContainer) { $records.Add("D|$relative") }
            else {
                $hash = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash
                $records.Add("F|$relative|$hash")
            }
        }
    }
    return (($records | Sort-Object) -join "`n")
}

# 范围与链接检查通过后移动单项；目标存在则失败，禁止嵌套或覆盖。
function Move-OutputEntry {
    param([string]$SourceRoot, [string]$TargetRoot, [string]$Name)
    $source = Assert-ChildPath -Path (Join-Path $SourceRoot $Name) -ExpectedParent $SourceRoot
    $target = Assert-ChildPath -Path (Join-Path $TargetRoot $Name) -ExpectedParent $TargetRoot
    Assert-PlainTree -Path $source
    Assert-PlainTree -Path $target
    if (Test-Path -LiteralPath $target) { throw "移动目标已存在，拒绝覆盖：$target" }
    if (Test-Path -LiteralPath $source -PathType Container) {
        [System.IO.Directory]::Move($source, $target)
    }
    else { [System.IO.File]::Move($source, $target) }
}

# 只清理本次临时或已确认可删除的备份目录，不清理正式发布根目录。
function Remove-TransactionDirectory {
    param([string]$Path, [string]$PublishParent)
    $safePath = Assert-ChildPath -Path $Path -ExpectedParent $PublishParent
    $name = Split-Path -Leaf $safePath
    if ($name -notmatch '^\.win-x64-(new|backup)-[0-9a-f]{32}$') {
        throw "拒绝清理非事务目录：$safePath"
    }
    Assert-PlainTree -Path $safePath
    if (Test-Path -LiteralPath $safePath) { Remove-Item -LiteralPath $safePath -Recurse -Force }
}

# 全部生成检查通过后才替换；失败则逆序恢复并核对旧快照。
function Install-PublishOutput {
    param($Context, $Stage, [string]$BackupRoot)
    $parent = Split-Path -Parent $Context.PublishRoot
    $expectedNew = Get-OutputSnapshot -Root $Stage.PublishRoot
    $expectedOld = Get-OutputSnapshot -Root $Context.PublishRoot
    Assert-PlainTree -Path $Context.PublishRoot
    $safeBackup = Assert-ChildPath -Path $BackupRoot -ExpectedParent $parent
    Assert-PlainTree -Path $safeBackup
    New-Item -ItemType Directory -Path $safeBackup | Out-Null
    # 保留恢复依据；强制关窗后，下次生成拒绝覆盖未处理的备份。
    @{ OriginalSnapshot = $expectedOld; Target = $Context.PublishRoot; Stage = $Stage.PublishRoot } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $safeBackup 'recovery-info.json') -Encoding UTF8
    $saved = [System.Collections.Generic.List[string]]::new()
    $installed = [System.Collections.Generic.List[string]]::new()
    $script:replacementStarted = $true
    try {
        New-Item -ItemType Directory -Force -Path $Context.PublishRoot | Out-Null
        foreach ($name in $script:managedNames) {
            if (Test-Path -LiteralPath (Join-Path $Context.PublishRoot $name)) {
                Move-OutputEntry -SourceRoot $Context.PublishRoot -TargetRoot $safeBackup -Name $name
                $saved.Add($name)
            }
            Move-OutputEntry -SourceRoot $Stage.PublishRoot -TargetRoot $Context.PublishRoot -Name $name
            $installed.Add($name)
        }
        Validate-PublishOutput -Context $Context
        if ((Get-OutputSnapshot -Root $Context.PublishRoot) -cne $expectedNew) {
            throw '替换后的程序与本次临时包不一致。'
        }
    }
    catch {
        $replaceError = $_.Exception.Message
        try {
            for ($i = $installed.Count - 1; $i -ge 0; $i--) {
                Move-OutputEntry -SourceRoot $Context.PublishRoot -TargetRoot $Stage.PublishRoot -Name $installed[$i]
            }
            for ($i = $saved.Count - 1; $i -ge 0; $i--) {
                Move-OutputEntry -SourceRoot $safeBackup -TargetRoot $Context.PublishRoot -Name $saved[$i]
            }
            if ((Get-OutputSnapshot -Root $Context.PublishRoot) -cne $expectedOld) {
                throw '恢复内容与替换前快照不一致。'
            }
        }
        catch {
            throw "替换失败：$replaceError；无法确认旧包恢复：$($_.Exception.Message)。请勿启动或再次覆盖正式包。正式目录：$($Context.PublishRoot)；备份：$safeBackup；临时包：$($Stage.PublishRoot)"
        }
        try { Remove-TransactionDirectory -Path $safeBackup -PublishParent $parent }
        catch { Write-Warning "旧产物已恢复，但备份清理失败，请核对后手动清理：$safeBackup。$($_.Exception.Message)" }
        throw "替换失败：$replaceError；已按快照恢复替换前产物（首次生成则恢复为无产物）。修复后可重试。"
    }
    try { Remove-TransactionDirectory -Path $safeBackup -PublishParent $parent }
    catch { Write-Warning "新包已生成并核对，但旧备份清理失败，请核对后手动清理：$safeBackup。$($_.Exception.Message)" }
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
    Write-Host "正在准备 Windows 联机服务器包..."
    Write-Host "请先停止使用正式包的服务器；本工具不会自动停服。"
    $context = Resolve-AndValidateContext -Root $GameServerRoot
    $parent = Split-Path -Parent $context.PublishRoot
    Assert-PlainTree -Path $parent
    $script:publishLock = Acquire-PublishLock -LockPath $context.LockPath
    $pendingBackups = @(Get-ChildItem -LiteralPath $parent -Force -Filter '.win-x64-backup-*')
    if ($pendingBackups.Count -gt 0) {
        throw "存在未处理的备份，请先核对正式包与备份、完成恢复或清理后重试：$($pendingBackups.FullName -join '；')"
    }
    $batch = [Guid]::NewGuid().ToString('N')
    $stageRoot = Assert-ChildPath -Path (Join-Path $parent ".win-x64-new-$batch") -ExpectedParent $parent
    $backupRoot = Assert-ChildPath -Path (Join-Path $parent ".win-x64-backup-$batch") -ExpectedParent $parent
    New-Item -ItemType Directory -Path $stageRoot | Out-Null
    $script:stageContext = New-OutputContext -Context $context -OutputRoot $stageRoot

    Invoke-WindowsPublish -Context $script:stageContext
    Copy-PublishConfig -Context $script:stageContext
    Copy-DeploymentLaunchers -Context $script:stageContext
    Validate-PublishOutput -Context $script:stageContext
    Install-PublishOutput -Context $context -Stage $script:stageContext -BackupRoot $backupRoot

    Write-Host ""
    Write-Host "联机服务器包生成成功，正式产物已核对。"
    Write-Host "完整目录：$($context.PublishRoot)"
    Write-Host "运行环境要求：目标设备需安装兼容的 .NET 8 Runtime。"
    Write-Host "部署整个目录后双击 启动联机服务器.bat；本工具不上传、不自动启动服务器。"
    Write-Host "Store 运行数据及其他非受管文件保持原位；请勿在正式包运行期间替换程序。"
    $script:exitCode = 0
}
catch {
    Write-Host ""
    Write-Host "联机服务器包生成失败。"
    Write-Host $_.Exception.Message
    if (-not $script:replacementStarted) {
        Write-Host "未进入正式包替换，本次未修改旧产物；若有上次遗留备份，应先核对恢复状态。"
    }
    $script:exitCode = 1
}
finally {
    # 恢复未确认时保留临时包与备份，避免丢失后续人工恢复所需文件。
    if ($null -ne $script:stageContext) {
        if (-not (Test-Path -LiteralPath $backupRoot)) {
            try { Remove-TransactionDirectory -Path $script:stageContext.PublishRoot -PublishParent $parent }
            catch { Write-Warning "临时目录清理失败：$($script:stageContext.PublishRoot)。$($_.Exception.Message)" }
        }
        else { Write-Host "已保留备份及临时目录供核对：$backupRoot；$($script:stageContext.PublishRoot)" }
    }
    Release-PublishLock
    Write-Host ""
    Write-Host "ExitCode=$script:exitCode"
    Read-Host "按 Enter 关闭窗口；修复失败原因后重新点击生成入口重试"
    exit $script:exitCode
}
