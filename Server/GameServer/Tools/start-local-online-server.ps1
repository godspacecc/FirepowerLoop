param(
    [Parameter(Mandatory = $true)]
    [string]$GameServerRoot,

    [int]$Port = 31011,

    [switch]$BuildBeforeStart
)

$ErrorActionPreference = "Stop"
$script:serverProcess = $null
$script:activeStop = $false
$script:state = "Preflight"
$script:logsPath = $null
$script:commandBuffer = [System.Text.StringBuilder]::new()
$script:redirectedReadTask = $null
$script:redirectedInputEnded = $false
$script:diagnosticPort = 31012

function Write-Info {
    param([string]$Message)
    Write-Host "[本地联机服务器] $Message"
}

function Resolve-Root {
    param([string]$Root)
    return [System.IO.Path]::GetFullPath($Root)
}

function Test-TcpPortFree {
    param([int]$Value)
    $listener = $null
    try {
        $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Parse("127.0.0.1"), $Value)
        $listener.Server.ExclusiveAddressUse = $true
        $listener.Start()
        return $true
    }
    catch {
        return $false
    }
    finally {
        if ($null -ne $listener) {
            $listener.Stop()
        }
    }
}

function Assert-DiagnosticHttpPermission {
    $listener = [System.Net.HttpListener]::new()
    try {
        $listener.Prefixes.Add("http://*:$($script:diagnosticPort)/")
        $listener.Start()
    }
    catch {
        $account = "$($env:USERDOMAIN)\$($env:USERNAME)"
        throw "当前账号无法监听诊断 HTTP 端口 $($script:diagnosticPort)：$($_.Exception.GetBaseException().Message)。请由管理员为运行服务器的账号登记 URL 权限：netsh http add urlacl url=http://*:$($script:diagnosticPort)/ user=`"$account`""
    }
    finally {
        $listener.Close()
    }
}

function Get-LanIPv4Addresses {
    $addresses = [System.Collections.Generic.List[string]]::new()
    foreach ($networkInterface in [System.Net.NetworkInformation.NetworkInterface]::GetAllNetworkInterfaces()) {
        if ($networkInterface.OperationalStatus -ne [System.Net.NetworkInformation.OperationalStatus]::Up) {
            continue
        }
        if ($networkInterface.NetworkInterfaceType -eq [System.Net.NetworkInformation.NetworkInterfaceType]::Loopback) {
            continue
        }

        $properties = $networkInterface.GetIPProperties()
        if ($properties.GatewayAddresses.Count -eq 0) {
            continue
        }
        foreach ($unicast in $properties.UnicastAddresses) {
            $address = $unicast.Address
            if ($address.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork) {
                continue
            }
            $text = $address.ToString()
            if ($text.StartsWith("169.254.") -or [System.Net.IPAddress]::IsLoopback($address)) {
                continue
            }
            if (-not $addresses.Contains($text)) {
                $addresses.Add($text)
            }
        }
    }
    return $addresses | Sort-Object
}

# 展示可复制到客户端的 GameGate 地址；不修改监听配置或判断远端可达性。
function Show-NetworkEndpoints {
    param([int]$Value)

    Write-Info "GameGate 监听: 0.0.0.0:${Value}"
    Write-Info "本机客户端联机地址: 127.0.0.1:${Value}"
    $lanAddresses = @(Get-LanIPv4Addresses)
    if ($lanAddresses.Count -eq 0) {
        Write-Info "局域网客户端联机地址: 未检测到"
        return
    }
    foreach ($address in $lanAddresses) {
        Write-Info "局域网客户端联机地址: ${address}:${Value}"
        Write-Info "局域网诊断入口: http://${address}:$($script:diagnosticPort)/diagnostics/errors（客户端局域网模式自动使用联机电脑地址）"
    }
    Write-Info "手机访问需在 Windows 专用网络放行 TCP ${Value} 和 $($script:diagnosticPort)。"
}

function Wait-PortReleased {
    param([int]$Value, [int]$TimeoutMs = 5000)
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    while ($watch.ElapsedMilliseconds -lt $TimeoutMs) {
        if (Test-TcpPortFree $Value) {
            return $true
        }
        Start-Sleep -Milliseconds 100
    }
    return (Test-TcpPortFree $Value)
}

function Show-Controls {
    Write-Info "按 R 重启此窗口拥有的 OnlineLocal 服务器；按 Ctrl+C 停止并退出；关闭窗口会停止附加的控制台进程；异常退出后可按 R 重启。"
    Write-Info "本地命令：netlog on、netlog off、netlog status。开启后密码等凭据会以原文进入窗口与 Debug 日志，off 不删除历史日志。"
}

function Invoke-Preflight {
    if ([string]::IsNullOrWhiteSpace($env:FIREPOWERLOOP_MONGODB_URI)) {
        throw "缺少 FIREPOWERLOOP_MONGODB_URI；请先配置本项目 MongoDB，旧身份文件须先显式迁移。"
    }
    $root = Resolve-Root $GameServerRoot
    $bin = Join-Path $root "Bin"
    $app = Join-Path $bin "App.dll"
    $hotfix = Join-Path $bin "Hotfix.dll"
    $nlog = Join-Path $root "Config/NLog/NLog.config"
    $luban = Join-Path $root "Config/Luban"
    $script:logsPath = Join-Path $root "Logs"

    if (-not (Test-Path -LiteralPath $root -PathType Container)) {
        throw "GameServerRoot 不存在：$root"
    }
    if (-not (Test-Path -LiteralPath $nlog -PathType Leaf)) {
        throw "NLog 配置不存在：$nlog"
    }
    if (-not (Test-Path -LiteralPath $luban -PathType Container)) {
        throw "Luban 配置目录不存在：$luban"
    }
    if ($null -eq (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw "当前环境未找到 dotnet 命令。"
    }
    if ($BuildBeforeStart) {
        $solution = Join-Path $root "ET.sln"
        $globalJson = Join-Path $root "global.json"
        $appProject = Join-Path $root "DotNet/App/DotNet.App.csproj"
        $hotfixProject = Join-Path $root "DotNet/Hotfix/DotNet.Hotfix.csproj"
        foreach ($requiredFile in @($solution, $globalJson, $appProject, $hotfixProject)) {
            if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
                throw "源码编译前置文件不存在：$requiredFile"
            }
        }

        Push-Location -LiteralPath $root
        try {
            $selectedSdk = (& dotnet --version 2>&1 | Out-String).Trim()
            $sdkExitCode = $LASTEXITCODE
        }
        finally {
            Pop-Location
        }
        if ($sdkExitCode -ne 0 -or $selectedSdk -notmatch '^8\.') {
            throw "global.json 未选择兼容的 .NET 8 SDK：Version=$selectedSdk，ExitCode=$sdkExitCode"
        }
    }
    elseif (-not (Test-Path -LiteralPath $app -PathType Leaf)) {
        throw "服务端 App.dll 不存在：$app"
    }
    if (-not (Test-TcpPortFree $Port)) {
        throw "127.0.0.1:${Port} 已被占用；未结束占用者。"
    }
    if (-not (Test-TcpPortFree $script:diagnosticPort)) {
        throw "127.0.0.1:$($script:diagnosticPort) 已被占用；未结束占用者。"
    }
    Assert-DiagnosticHttpPermission

    return @{
        Root = $root
        Bin = $bin
        App = $app
        Hotfix = $hotfix
        Command = "dotnet App.dll --AppType=Server --StartConfig=OnlineLocal --Console=1"
    }
}

function Invoke-SourceBuild {
    param([hashtable]$Context)

    Write-Info "开始增量编译：dotnet build ET.sln -c Release -p:NuGetAudit=false"
    Push-Location -LiteralPath $Context.Root
    try {
        & dotnet build ET.sln -c Release -p:NuGetAudit=false
        $buildExitCode = $LASTEXITCODE
    }
    catch {
        throw "源码编译阶段异常：$($_.Exception.Message)"
    }
    finally {
        Pop-Location
    }

    if ($buildExitCode -ne 0) {
        throw "源码编译失败：ExitCode=$buildExitCode；禁止启动旧程序集。"
    }
    Write-Info "增量编译成功：ExitCode=0"

    $missingAssemblies = @()
    if (-not (Test-Path -LiteralPath $Context.App -PathType Leaf)) {
        $missingAssemblies += $Context.App
    }
    if (-not (Test-Path -LiteralPath $Context.Hotfix -PathType Leaf)) {
        $missingAssemblies += $Context.Hotfix
    }
    if ($missingAssemblies.Count -gt 0) {
        throw "编译产物校验失败，缺少必需程序集：$($missingAssemblies -join '；')；禁止启动旧程序集。"
    }
    Write-Info "编译产物校验成功：App.dll、Hotfix.dll"

    if (-not (Test-TcpPortFree $Port)) {
        throw "编译后 127.0.0.1:${Port} 已被占用；未启动服务器，未结束占用者。"
    }
    if (-not (Test-TcpPortFree $script:diagnosticPort)) {
        throw "编译后 127.0.0.1:$($script:diagnosticPort) 已被占用；未启动服务器，未结束占用者。"
    }
}

function Start-Server {
    param([hashtable]$Context)
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = "dotnet"
    $startInfo.Arguments = "App.dll --AppType=Server --StartConfig=OnlineLocal --Console=1"
    $startInfo.WorkingDirectory = $Context.Bin
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $false
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $false
    $startInfo.RedirectStandardError = $false

    Write-Info "命令: $($Context.Command)"
    Write-Info "WorkingDirectory: $($Context.Bin)"
    Show-NetworkEndpoints -Value $Port
    Write-Info "诊断 HTTP 配置: http://*:$($script:diagnosticPort)/（与联机服务一同启动）"
    Write-Info "日志目录: $script:logsPath"

    $script:serverProcess = [System.Diagnostics.Process]::Start($startInfo)
    if ($null -eq $script:serverProcess) {
        throw "Process.Start 未返回服务器进程。"
    }

    Write-Info "服务器进程已启动：PID=$($script:serverProcess.Id)"
}

function Close-ServerInput {
    if ($null -eq $script:serverProcess) {
        return
    }

    try {
        $script:serverProcess.StandardInput.Flush()
    }
    catch {
        Write-Host "[本地联机服务器] 关闭服务器输入前 Flush 失败：$($_.Exception.GetType().Name)"
    }
    try {
        $script:serverProcess.StandardInput.Close()
        $script:serverProcess.StandardInput.Dispose()
    }
    catch {
        Write-Host "[本地联机服务器] 关闭服务器输入失败：$($_.Exception.GetType().Name)"
    }
}

function Stop-OwnedServer {
    if ($null -eq $script:serverProcess) {
        return
    }

    try {
        Close-ServerInput
        $script:serverProcess.Refresh()
        if (-not $script:serverProcess.HasExited) {
            Write-Info "正在停止此窗口拥有的服务器进程：PID=$($script:serverProcess.Id)"
            $script:serverProcess.Kill()
            if (-not $script:serverProcess.WaitForExit(5000)) {
                Write-Host "[本地联机服务器] 服务器进程在超时前未退出，请检查此窗口与日志目录。"
            }
        }
    }
    catch {
        Write-Host "[本地联机服务器] 停止自有进程失败：$($_.Exception.Message)"
    }
    finally {
        $script:serverProcess.Dispose()
        $script:serverProcess = $null
        [void](Wait-PortReleased $Port 5000)
        [void](Wait-PortReleased $script:diagnosticPort 5000)
    }
}

function Request-Restart {
    if ($script:state -eq "Running") {
        $script:activeStop = $true
        Stop-OwnedServer
        $script:activeStop = $false
        $script:state = "Preflight"
    }
    elseif ($script:state -eq "WaitingAfterFailure") {
        $script:state = "Preflight"
    }
}

function Dispatch-InputLine {
    param([AllowEmptyString()][string]$Line)

    if ([string]::IsNullOrWhiteSpace($Line)) {
        return
    }
    if ([string]::Equals($Line.Trim(), "R", [System.StringComparison]::OrdinalIgnoreCase)) {
        Request-Restart
        return
    }
    if ($script:state -ne "Running" -or $null -eq $script:serverProcess) {
        Write-Info "服务器未运行，命令未发送。"
        return
    }

    try {
        $script:serverProcess.Refresh()
        if ($script:serverProcess.HasExited -or $script:serverProcess.StandardInput.BaseStream.CanWrite -eq $false) {
            throw [System.IO.IOException]::new("服务器标准输入不可写。")
        }
        $script:serverProcess.StandardInput.WriteLine($Line)
        $script:serverProcess.StandardInput.Flush()
    }
    catch {
        Write-Info "命令发送失败：$($_.Exception.GetType().Name)"
    }
}

function Read-InteractiveInput {
    if (-not [Console]::KeyAvailable) {
        return $false
    }

    $key = [Console]::ReadKey($true)
    if (($key.Modifiers -band [ConsoleModifiers]::Control) -and $key.Key -eq [ConsoleKey]::C) {
        return $true
    }
    if ($key.Key -eq [ConsoleKey]::R -and $script:commandBuffer.Length -eq 0) {
        Request-Restart
        return $false
    }
    if ($key.Key -eq [ConsoleKey]::Backspace) {
        if ($script:commandBuffer.Length -gt 0) {
            $script:commandBuffer.Remove($script:commandBuffer.Length - 1, 1) | Out-Null
            [Console]::Write("`b `b")
        }
        return $false
    }
    if ($key.Key -eq [ConsoleKey]::Enter) {
        [Console]::WriteLine()
        $line = $script:commandBuffer.ToString()
        [void]$script:commandBuffer.Clear()
        Dispatch-InputLine $line
        return $false
    }
    if (-not [char]::IsControl($key.KeyChar)) {
        [void]$script:commandBuffer.Append($key.KeyChar)
        [Console]::Write($key.KeyChar)
    }
    return $false
}

function Read-RedirectedInput {
    if ($script:redirectedInputEnded) {
        return
    }
    if ($null -eq $script:redirectedReadTask) {
        $script:redirectedReadTask = [Console]::In.ReadLineAsync()
        return
    }
    if (-not $script:redirectedReadTask.IsCompleted) {
        return
    }

    $line = $script:redirectedReadTask.GetAwaiter().GetResult()
    $script:redirectedReadTask = $null
    if ($null -eq $line) {
        $script:redirectedInputEnded = $true
        return
    }
    Dispatch-InputLine $line
}

function Enter-WaitingAfterFailure {
    param([string]$Reason)
    Write-Host ""
    Write-Host "[本地联机服务器] 服务器未运行：$Reason"
    Write-Host "[本地联机服务器] 日志目录: $script:logsPath"
    Show-Controls
    $script:state = "WaitingAfterFailure"
}

try {
    $inputIsRedirected = [Console]::IsInputRedirected
    $originalTreatControlCAsInput = $null
    if (-not $inputIsRedirected) {
        $originalTreatControlCAsInput = [Console]::TreatControlCAsInput
        [Console]::TreatControlCAsInput = $true
    }
    [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
    [Console]::InputEncoding = [System.Text.UTF8Encoding]::new($false)
    $Host.UI.RawUI.WindowTitle = "FirepowerLoop 本地联机服务器 OnlineLocal"

    Write-Info "本地联机服务器启动窗口已就绪。"
    Show-Controls

    while ($true) {
        if ($script:state -eq "Preflight") {
            try {
                $context = Invoke-Preflight
                if ($BuildBeforeStart) {
                    Invoke-SourceBuild $context
                }
                Start-Server $context
                $script:state = "Running"
            }
            catch {
                Enter-WaitingAfterFailure $_.Exception.Message
            }
        }

        if ($inputIsRedirected) {
            Read-RedirectedInput
        }
        else {
            if (Read-InteractiveInput) {
                $script:activeStop = $true
                Stop-OwnedServer
                break
            }
        }

        if ($script:state -eq "Running" -and $null -ne $script:serverProcess) {
            $script:serverProcess.Refresh()
            if ($script:serverProcess.HasExited) {
                $exitCode = $script:serverProcess.ExitCode
                Close-ServerInput
                $script:serverProcess.Dispose()
                $script:serverProcess = $null
                if (-not $script:activeStop) {
                    Enter-WaitingAfterFailure "服务器异常退出，ExitCode=$exitCode"
                }
                $script:activeStop = $false
            }
        }

        Start-Sleep -Milliseconds 100
    }
}
finally {
    Stop-OwnedServer
    if ($null -ne $originalTreatControlCAsInput) {
        [Console]::TreatControlCAsInput = $originalTreatControlCAsInput
    }
}
