param([switch]$StoreOnly)

$ErrorActionPreference = 'Stop'
$serverRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$tempRoot = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), 'AnotherDay-DiagnosticTests-' + [guid]::NewGuid().ToString('N'))
$ownedProcess = $null

function Assert-PortFree([int]$port) {
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $port)
    try {
        $listener.Server.ExclusiveAddressUse = $true
        $listener.Start()
    }
    catch {
        throw "端口 $port 已占用，未启动测试服务。"
    }
    finally {
        $listener.Stop()
    }
}

try {
    Assert-PortFree 31011
    Assert-PortFree 31012
    [System.IO.Directory]::CreateDirectory($tempRoot) | Out-Null
    Push-Location -LiteralPath $serverRoot
    try {
        & dotnet build ET.sln -c Release -p:NuGetAudit=false --nologo -v:q
        if ($LASTEXITCODE -ne 0) { throw "服务端编译失败：$LASTEXITCODE" }
    }
    finally { Pop-Location }

    # HTTP.sys 不可用时仍验证存储边界；所有数据只写本脚本的隔离目录。
    Add-Type -Path (Join-Path $serverRoot 'Bin/Model.dll')
    $now = [DateTimeOffset]::UtcNow
    $report = [ET.Server.DiagnosticReport]::new()
    $report.Source = 'Runtime'; $report.Kind = 'Error'; $report.Fingerprint = 'a' * 64
    $report.BuildVersion = 'test'; $report.Platform = 'WindowsPlayer'
    $report.EventDate = $now.ToString('yyyy-MM-dd'); $report.FirstUtc = $now; $report.LastUtc = $now
    $report.Count = 1; $report.BootCode = ''; $report.BootStage = ''
    $batch = [ET.Server.DiagnosticBatch]::new()
    $batch.BatchId = [guid]::NewGuid().ToString('D')
    $batch.Reports = [System.Collections.Generic.List[ET.Server.DiagnosticReport]]::new()
    $batch.Reports.Add($report)
    $directRoot = Join-Path $tempRoot 'DirectStore'
    $direct = [ET.Server.DiagnosticReportStore]::new($directRoot)
    $loopback = [System.Net.IPAddress]::Loopback
    if ($direct.Accept($batch, $loopback, '', '', $now) -ne 200) { throw '存储首次确认失败。' }
    if ($direct.Accept($batch, $loopback, '', '', $now) -ne 200) { throw '同批次重复确认失败。' }
    $restarted = [ET.Server.DiagnosticReportStore]::new($directRoot)
    if ($restarted.Accept($batch, $loopback, '', '', $now) -ne 200) { throw '重启后重复确认失败。' }
    $report.Count = 2
    if ($restarted.Accept($batch, $loopback, '', '', $now) -ne 409) { throw '同 ID 异内容未拒绝。' }
    $batch.BatchId = [guid]::NewGuid().ToString('D')
    if ($restarted.Accept($batch, $loopback, '', '127.0.0.1', $now) -ne 400) { throw '可信代理缺少 X-Real-IP 未拒绝。' }
    if ($restarted.Accept($batch, $loopback, 'invalid,127.0.0.1', '127.0.0.1', $now) -ne 400) { throw '多值代理头未拒绝。' }
    $logPath = Join-Path $directRoot ($now.ToString('yyyyMMdd') + '.jsonl')
    [System.IO.File]::AppendAllText($logPath, '{"incomplete":')
    $repaired = [ET.Server.DiagnosticReportStore]::new($directRoot)
    $report.Count = 1
    $batch.BatchId = [System.IO.File]::ReadAllLines($logPath)[0] | ConvertFrom-Json | Select-Object -ExpandProperty BatchId
    if ($repaired.Accept($batch, $loopback, '', '', $now) -ne 200) { throw '截断尾行后原批次去重失败。' }
    if ([System.IO.File]::ReadAllText($logPath).Contains('incomplete')) { throw '截断尾行未持久修复。' }
    for ($i = 0; $i -lt 60; $i++) {
        $batch.BatchId = [guid]::NewGuid().ToString('D')
        if ($repaired.Accept($batch, $loopback, '203.0.113.1', '', $now) -ne 200) { throw "直连第 $i 次有效请求失败。" }
    }
    $batch.BatchId = [guid]::NewGuid().ToString('D')
    if ($repaired.Accept($batch, $loopback, '', '', $now) -ne 429) { throw '每分钟第 61 次请求未限流。' }
    [System.IO.File]::AppendAllText($logPath, "{`"bad`":true}`n")
    $invalid = [ET.Server.DiagnosticReportStore]::new($directRoot)
    $batch.BatchId = [guid]::NewGuid().ToString('D')
    if ($invalid.Accept($batch, $loopback, '', '', $now) -ne 503) { throw '完整损坏行未阻止新批次确认。' }
    $retentionRoot = Join-Path $tempRoot 'RetentionStore'
    $oldUtc = $now.AddDays(-31)
    $oldReport = [ET.Server.DiagnosticReport]::new()
    $oldReport.Source = 'Runtime'; $oldReport.Kind = 'Error'; $oldReport.Fingerprint = 'b' * 64
    $oldReport.BuildVersion = 'test'; $oldReport.Platform = 'WindowsPlayer'
    $oldReport.EventDate = $oldUtc.ToString('yyyy-MM-dd')
    $oldReport.FirstUtc = $oldUtc; $oldReport.LastUtc = $oldUtc
    $oldReport.Count = 1; $oldReport.BootCode = ''; $oldReport.BootStage = ''
    $oldBatch = [ET.Server.DiagnosticBatch]::new()
    $oldBatch.BatchId = [guid]::NewGuid().ToString('D')
    $oldBatch.Reports = [System.Collections.Generic.List[ET.Server.DiagnosticReport]]::new()
    $oldBatch.Reports.Add($oldReport)
    $oldStore = [ET.Server.DiagnosticReportStore]::new($retentionRoot)
    if ($oldStore.Accept($oldBatch, $loopback, '', '', $oldUtc) -ne 200) { throw '旧日期隔离样本未写入。' }
    $oldPath = Join-Path $retentionRoot ($oldUtc.ToString('yyyyMMdd') + '.jsonl')
    if (-not (Test-Path -LiteralPath $oldPath)) { throw '旧日期隔离样本文件缺失。' }
    $currentStore = [ET.Server.DiagnosticReportStore]::new($retentionRoot)
    $batch.BatchId = [guid]::NewGuid().ToString('D')
    if ($currentStore.Accept($batch, $loopback, '', '', $now) -ne 200 -or (Test-Path -LiteralPath $oldPath)) {
        throw '30 天保留窗口未回收旧日期文件。'
    }
    Write-Host 'PASS: 隔离存储持久确认、重启幂等、409、代理头、尾行修复、60 次限流、损坏行 503 与 30 天保留。'

    Add-Type -Path (Join-Path $serverRoot 'Bin/Hotfix.dll')
    $parser = [ET.Server.DiagnosticReportHandler].GetMethod('TryParse',
        [System.Reflection.BindingFlags]::NonPublic -bor [System.Reflection.BindingFlags]::Static)
    $validPayload = @{ schemaVersion = 1; batchId = [guid]::NewGuid().ToString('D'); reports = @(@{
        source = 'Runtime'; kind = 'Error'; fingerprint = ('a' * 64); buildVersion = 'test'
        platform = 'WindowsPlayer'; eventDate = $now.ToString('yyyy-MM-dd')
        firstUtc = $now.ToString('O'); lastUtc = $now.ToString('O')
        count = 1; bootCode = ''; bootStage = ''
    }) } | ConvertTo-Json -Depth 5 -Compress
    $parseArgs = @([System.Text.Encoding]::UTF8.GetBytes($validPayload), $null)
    if (-not $parser.Invoke($null, $parseArgs)) { throw '合法匿名 JSON 解析失败。' }
    $duplicateField = $validPayload.Replace('"source":"Runtime"', '"source":"Runtime","source":"Runtime"')
    $parseArgs = @([System.Text.Encoding]::UTF8.GetBytes($duplicateField), $null)
    if ($parser.Invoke($null, $parseArgs)) { throw '重复 JSON 字段未拒绝。' }
    $unknownField = $validPayload.Replace('"source":"Runtime"', '"source":"Runtime","message":"secret"')
    $parseArgs = @([System.Text.Encoding]::UTF8.GetBytes($unknownField), $null)
    if ($parser.Invoke($null, $parseArgs)) { throw '原始日志字段未拒绝。' }
    Write-Host 'PASS: 处理器解析合法匿名 JSON，并拒绝重复字段与原始消息字段。'
    if ($StoreOnly) { return }

    $start = [System.Diagnostics.ProcessStartInfo]::new('dotnet')
    $start.WorkingDirectory = Join-Path $serverRoot 'Bin'
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.ArgumentList.Add('App.dll')
    $start.ArgumentList.Add('--AppType=Server')
    $start.ArgumentList.Add('--StartConfig=OnlineLocal')
    $start.ArgumentList.Add('--Console=0')
    $start.ArgumentList.Add("--OnlineStore=$tempRoot/Online/identity-store.json")
    $start.ArgumentList.Add("--OnlineArcadeStore=$tempRoot/Online/arcade-runs.json")
    $start.ArgumentList.Add("--DiagnosticStoreRoot=$tempRoot/Diagnostics")
    $ownedProcess = [System.Diagnostics.Process]::Start($start)
    if ($null -eq $ownedProcess) { throw '测试服务启动失败。' }

    $ready = $false
    for ($attempt = 0; $attempt -lt 100; $attempt++) {
        $ownedProcess.Refresh()
        if ($ownedProcess.HasExited) { throw "测试服务提前退出：$($ownedProcess.ExitCode)" }
        $port = Get-NetTCPConnection -LocalPort 31012 -State Listen -ErrorAction SilentlyContinue
        if ($port) { $ready = $true; break }
        Start-Sleep -Milliseconds 100
    }
    if (-not $ready) {
        $recent = Get-ChildItem -LiteralPath (Join-Path $serverRoot 'Logs') -Filter '*RouterManager*Error.log' -File -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
        if ($recent) { throw "诊断 HTTP 端口 31012 未启动。最近 RouterManager 错误日志（$($recent.LastWriteTime)）：$(Get-Content -LiteralPath $recent.FullName -Tail 8 | Out-String)" }
        throw '诊断 HTTP 端口 31012 未启动，且未找到 RouterManager 错误日志。'
    }

    $now = [DateTimeOffset]::UtcNow
    $payload = @{
        schemaVersion = 1
        batchId = [guid]::NewGuid().ToString()
        reports = @(@{
            source = 'Runtime'; kind = 'Error'; fingerprint = ('a' * 64)
            buildVersion = 'test'; platform = 'WindowsPlayer'
            eventDate = $now.ToString('yyyy-MM-dd')
            firstUtc = $now.ToString('O'); lastUtc = $now.ToString('O')
            count = 1; bootCode = ''; bootStage = ''
        })
    } | ConvertTo-Json -Depth 5 -Compress
    $http = [System.Net.Http.HttpClient]::new()
    try {
        $body = [System.Net.Http.StringContent]::new($payload, [System.Text.Encoding]::UTF8, 'application/json')
        $response = $http.PostAsync('http://127.0.0.1:31012/diagnostics/errors', $body).GetAwaiter().GetResult()
        if ([int]$response.StatusCode -ne 200) { throw "合法诊断请求状态码：$([int]$response.StatusCode)" }
        Write-Host 'PASS: 合法诊断批次返回 200。'

        $same = $http.PostAsync('http://127.0.0.1:31012/diagnostics/errors',
            [System.Net.Http.StringContent]::new($payload, [System.Text.Encoding]::UTF8, 'application/json')).GetAwaiter().GetResult()
        if ([int]$same.StatusCode -ne 200) { throw "同批次重复请求状态码：$([int]$same.StatusCode)" }
        $conflictPayload = $payload.Replace('"count":1', '"count":2')
        if ($conflictPayload -eq $payload) { throw '无法构造同 ID 异内容批次。' }
        $conflict = $http.PostAsync('http://127.0.0.1:31012/diagnostics/errors',
            [System.Net.Http.StringContent]::new($conflictPayload, [System.Text.Encoding]::UTF8, 'application/json')).GetAwaiter().GetResult()
        if ([int]$conflict.StatusCode -ne 409) { throw "同 ID 异内容状态码：$([int]$conflict.StatusCode)" }
        $wrongMethod = $http.GetAsync('http://127.0.0.1:31012/diagnostics/errors').GetAwaiter().GetResult()
        if ([int]$wrongMethod.StatusCode -ne 405) { throw "非 POST 状态码：$([int]$wrongMethod.StatusCode)" }
        $duplicate = $http.PostAsync('http://127.0.0.1:31012/diagnostics/errors',
            [System.Net.Http.StringContent]::new($duplicateField, [System.Text.Encoding]::UTF8, 'application/json')).GetAwaiter().GetResult()
        if ([int]$duplicate.StatusCode -ne 400) { throw "重复字段状态码：$([int]$duplicate.StatusCode)" }
        $large = $http.PostAsync('http://127.0.0.1:31012/diagnostics/errors',
            [System.Net.Http.StringContent]::new(('x' * 17000), [System.Text.Encoding]::UTF8, 'application/json')).GetAwaiter().GetResult()
        if ([int]$large.StatusCode -ne 413) { throw "Content-Length 超限状态码：$([int]$large.StatusCode)" }
        $chunked = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post,
            'http://127.0.0.1:31012/diagnostics/errors')
        $chunked.Headers.TransferEncodingChunked = $true
        $chunked.Content = [System.Net.Http.ByteArrayContent]::new([byte[]]([System.Text.Encoding]::UTF8.GetBytes('x' * 17000)))
        $chunked.Content.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse('application/json')
        $chunkedResponse = $http.SendAsync($chunked).GetAwaiter().GetResult()
        if ([int]$chunkedResponse.StatusCode -ne 413) { throw "分块超限状态码：$([int]$chunkedResponse.StatusCode)" }
        $stats = @(& (Join-Path $serverRoot 'Tools/get-diagnostic-stats.ps1') -StoreRoot (Join-Path $tempRoot 'Diagnostics'))
        if ($stats.Count -ne 1 -or $stats[0].Count -ne 1) { throw 'HTTP 重试批次被重复计数。' }
        $gate = [System.Net.Sockets.TcpClient]::new()
        try { $gate.Connect('127.0.0.1', 31011) } finally { $gate.Dispose() }
        Write-Host 'PASS: HTTP 200/409/405/400/413、分块超限、重试去重和 GameGate 31011 TCP 连接。'
        for ($i = 0; $i -lt 59; $i++) {
            $rateRequest = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post,
                'http://127.0.0.1:31012/diagnostics/errors')
            $rateRequest.Headers.Add('X-Real-IP', "198.51.100.$($i + 1)")
            $ratePayload = $payload.Replace([string]($payload | ConvertFrom-Json).batchId, [guid]::NewGuid().ToString('D'))
            $rateRequest.Content = [System.Net.Http.StringContent]::new($ratePayload, [System.Text.Encoding]::UTF8, 'application/json')
            $rateResponse = $http.SendAsync($rateRequest).GetAwaiter().GetResult()
            if ([int]$rateResponse.StatusCode -ne 200) { throw "第 $i 次 HTTP 限流预热状态码：$([int]$rateResponse.StatusCode)" }
            $rateRequest.Dispose()
        }
        $limited = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post,
            'http://127.0.0.1:31012/diagnostics/errors')
        $limited.Headers.Add('X-Real-IP', '198.51.100.250')
        $limited.Content = [System.Net.Http.StringContent]::new(
            $payload.Replace([string]($payload | ConvertFrom-Json).batchId, [guid]::NewGuid().ToString('D')),
            [System.Text.Encoding]::UTF8, 'application/json')
        $limitedResponse = $http.SendAsync($limited).GetAwaiter().GetResult()
        if ([int]$limitedResponse.StatusCode -ne 429) { throw "第 61 次 HTTP 请求状态码：$([int]$limitedResponse.StatusCode)" }
        $limited.Dispose()
        Write-Host 'PASS: 60 次/分 HTTP 限流返回 429，直连伪造 X-Real-IP 不改变限流身份。'
    }
    finally { $http.Dispose() }
}
finally {
    if ($null -ne $ownedProcess) {
        $ownedProcess.Refresh()
        if (-not $ownedProcess.HasExited) {
            $ownedProcess.Kill()
            [void]$ownedProcess.WaitForExit(5000)
        }
        $ownedProcess.Dispose()
    }
    $safeParent = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    $safeTarget = [System.IO.Path]::GetFullPath($tempRoot)
    if ($safeTarget.StartsWith($safeParent, [System.StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $safeTarget)) {
        Remove-Item -LiteralPath $safeTarget -Recurse -Force
    }
}
