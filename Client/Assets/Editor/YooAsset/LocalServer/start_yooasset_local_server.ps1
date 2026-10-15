param(
    [int]$Port = 8001,
    [Parameter(Mandatory=$true)]
    [string]$Root,
    [switch]$ListenOnLan
)

$Listener = $null

# Android 菜单仍由同一启动器管理进程，但发布包必须提供资源、公告和 APK 的完整路由。
# 缺少完整脚本时直接失败，不能静默降级成只有资源文件的服务器。
if ((Split-Path $Root -Leaf) -eq 'ResourceFiles') {
    $publishedRoot = Split-Path ([System.IO.Path]::GetFullPath($Root)) -Parent
    $publishedScript = Join-Path $publishedRoot 'resource_server.ps1'
    if (-not (Test-Path -LiteralPath $publishedScript -PathType Leaf)) {
        throw "Published resource server script is missing: $publishedScript"
    }
    $listenAddress = if ($ListenOnLan) { '0.0.0.0' } else { '127.0.0.1' }
    & $publishedScript -Port $Port -Root $Root -ListenAddress $listenAddress `
        -NoticeFile (Join-Path $publishedRoot 'Notices/resource-update-notices.json') `
        -AppUpdateRoot (Join-Path $publishedRoot 'AppUpdates/Android')
    return
}

# 按当前监听范围展示客户端根地址，不修改客户端配置或检查远端可达性。
function Show-NetworkEndpoints {
    param([int]$Value, [bool]$AllowLan)

    Write-Host "本机客户端资源地址: http://127.0.0.1:${Value}"
    if (-not $AllowLan) {
        return
    }

    $addresses = [System.Collections.Generic.List[string]]::new()
    try {
        foreach ($networkInterface in [System.Net.NetworkInformation.NetworkInterface]::GetAllNetworkInterfaces()) {
            if ($networkInterface.OperationalStatus -ne [System.Net.NetworkInformation.OperationalStatus]::Up -or
                $networkInterface.NetworkInterfaceType -eq [System.Net.NetworkInformation.NetworkInterfaceType]::Loopback) {
                continue
            }
            foreach ($unicast in $networkInterface.GetIPProperties().UnicastAddresses) {
                $address = $unicast.Address
                if ($address.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork -or
                    [System.Net.IPAddress]::IsLoopback($address)) {
                    continue
                }
                $text = $address.ToString()
                if (-not $text.StartsWith('169.254.') -and -not $addresses.Contains($text)) {
                    $addresses.Add($text)
                }
            }
        }
    }
    catch {
        # 地址提示不是启动前置条件，网卡枚举失败时仍继续运行资源服。
        Write-Host "局域网地址读取失败。"
    }
    foreach ($address in ($addresses | Sort-Object)) {
        Write-Host "局域网客户端资源地址: http://${address}:${Value}"
    }
    if ($addresses.Count -eq 0) {
        Write-Host "局域网客户端资源地址: 未检测到"
    }
}

function Get-MimeType($Path) {
    $ext = [System.IO.Path]::GetExtension($Path).ToLowerInvariant()
    switch ($ext) {
        '.html' { 'text/html'; break }
        '.htm' { 'text/html'; break }
        '.css' { 'text/css'; break }
        '.js' { 'application/javascript'; break }
        '.json' { 'application/json'; break }
        '.xml' { 'application/xml'; break }
        '.txt' { 'text/plain'; break }
        '.version' { 'text/plain'; break }
        default { 'application/octet-stream'; break }
    }
}

function Send-Response($Stream, $StatusCode, $StatusText, $Headers, $Body) {
    if (-not $Headers) { $Headers = @{} }
    $Headers['Connection'] = 'close'
    $Header = "HTTP/1.1 $StatusCode $StatusText`r`n"
    foreach ($key in $Headers.Keys) {
        $Header += "${key}: $($Headers[$key])`r`n"
    }
    $Header += "`r`n"

    $HeaderBytes = [System.Text.Encoding]::ASCII.GetBytes($Header)
    $Stream.Write($HeaderBytes, 0, $HeaderBytes.Length)
    if ($Body -and $Body.Length -gt 0) {
        $Stream.Write($Body, 0, $Body.Length)
    }
}

try {
    if (-not (Test-Path $Root -PathType Container)) {
        throw "Root directory does not exist: $Root"
    }

    $Root = [System.IO.Path]::GetFullPath((Resolve-Path $Root).Path).TrimEnd('\', '/')
    $RootPrefix = $Root + [System.IO.Path]::DirectorySeparatorChar

    $VersionFile = Join-Path $Root 'DefaultPackage.version'
    if (-not (Test-Path $VersionFile -PathType Leaf)) {
        throw "Version file does not exist: $VersionFile"
    }
    $Version = (Get-Content -Raw $VersionFile).Trim()

    try {
        $Host.UI.RawUI.WindowTitle = "Another Day YooAsset资源服 - $Port"
    } catch {
        Write-Warning "Unable to set console title: $_"
    }

    Write-Host "========================================"
    Write-Host "Another Day YooAsset资源服"
    Write-Host "资源版本: $Version"
    Write-Host "资源目录: $Root"
    $ListenAddress = if ($ListenOnLan) { [System.Net.IPAddress]::Any } else { [System.Net.IPAddress]::Loopback }
    $DisplayHost = if ($ListenOnLan) { '0.0.0.0' } else { '127.0.0.1' }
    Write-Host "监听地址: http://$DisplayHost`:$Port"
    Show-NetworkEndpoints -Value $Port -AllowLan ([bool]$ListenOnLan)
    Write-Host "版本地址: http://127.0.0.1:$Port/DefaultPackage.version"
    Write-Host "停止服务: 按 Ctrl+C 或关闭窗口"
    Write-Host "========================================"

    $Listener = [System.Net.Sockets.TcpListener]::new($ListenAddress, $Port)
    $Listener.Start()

    while ($true) {
        $Client = $Listener.AcceptTcpClient()
        $Stream = $Client.GetStream()

        try {
            $Buffer = New-Object byte[] 4096
            $BytesRead = $Stream.Read($Buffer, 0, $Buffer.Length)
            if ($BytesRead -le 0) {
                Send-Response $Stream 400 "Bad Request" @{} $null
                $Client.Close()
                continue
            }

            $RequestText = [System.Text.Encoding]::ASCII.GetString($Buffer, 0, $BytesRead)
            $Lines = $RequestText -split "`r`n"
            $Parts = $Lines[0] -split ' '
            if ($Parts.Length -lt 3) {
                Send-Response $Stream 400 "Bad Request" @{} $null
                $Client.Close()
                continue
            }

            $Method = $Parts[0].ToUpperInvariant()
            $Url = [System.Uri]::UnescapeDataString($Parts[1])
            $QueryIndex = $Url.IndexOf('?')
            if ($QueryIndex -ge 0) { $Url = $Url.Substring(0, $QueryIndex) }
            $Url = $Url.TrimStart('/')

            $FullPath = [System.IO.Path]::GetFullPath((Join-Path $Root $Url))
            $InsideRoot = $FullPath.Equals($Root, [System.StringComparison]::OrdinalIgnoreCase) -or
                          $FullPath.StartsWith($RootPrefix, [System.StringComparison]::OrdinalIgnoreCase)
            if (-not $InsideRoot) {
                Send-Response $Stream 403 "Forbidden" @{} $null
                $Client.Close()
                continue
            }

            if ($Method -ne 'GET' -and $Method -ne 'HEAD') {
                Send-Response $Stream 405 "Method Not Allowed" @{} $null
                $Client.Close()
                continue
            }

            if (-not (Test-Path $FullPath -PathType Leaf)) {
                Send-Response $Stream 404 "Not Found" @{} $null
                $Client.Close()
                continue
            }

            $FileInfo = Get-Item $FullPath
            $Headers = @{
                'Content-Type' = Get-MimeType $FullPath
                'Content-Length' = $FileInfo.Length
            }

            if ($Method -eq 'HEAD') {
                Send-Response $Stream 200 "OK" $Headers $null
            } else {
                $FileBytes = [System.IO.File]::ReadAllBytes($FullPath)
                Send-Response $Stream 200 "OK" $Headers $FileBytes
            }
            $Client.Close()
        } catch {
            try {
                Send-Response $Stream 500 "Internal Server Error" @{} $null
                $Client.Close()
            } catch { }
        }
    }
} catch {
    Write-Error "[Fatal] $_"
    exit 1
} finally {
    if ($Listener) { $Listener.Stop() }
}
