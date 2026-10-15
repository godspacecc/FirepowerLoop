param([string]$Tool, [string]$ArgumentsFile, [string]$OutputFile, [int]$MaxTests = 0)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$argumentsObject = if ($ArgumentsFile) { Get-Content -LiteralPath $ArgumentsFile -Raw | ConvertFrom-Json -AsHashtable } else { @{} }
if ($Tool -eq 'tests-run') {
    if ($MaxTests -le 0 -or $argumentsObject.testMode -ne 'EditMode' -or !$argumentsObject.includePassingTests) { throw 'Explicit bounded EditMode scope and includePassingTests=true required' }
    if (!$argumentsObject.testClass -and !$argumentsObject.testMethod) { throw 'Real testClass or full testMethod required' }
    $fixture = if ($argumentsObject.testClass) { $argumentsObject.testClass } else { ($argumentsObject.testMethod -split '\.')[-2] }
    $files = @(Get-ChildItem (Join-Path $root 'Client/Assets/Tests/EditMode') -Filter "$fixture.cs" -Recurse)
    if ($files.Count -ne 1) { throw 'Unknown or ambiguous fixture: refusing fallback' }
    $source = Get-Content -LiteralPath $files[0].FullName -Raw
    if ($argumentsObject.testMethod) {
        $method = ($argumentsObject.testMethod -split '\.')[-1]
        if ($source -notmatch ('\b' + [regex]::Escape($method) + '\(')) { throw 'Unknown method: refusing fallback' }
    } else {
        $count = [regex]::Matches($source, '\[(?:UnityTest|Test|TestCase)(?:\]|\()').Count
        if ($count -le 0 -or $count -gt $MaxTests) { throw "Declared $count cases exceed MaxTests=$MaxTests" }
    }
}
$config = Get-Content (Join-Path $root 'Client/UserSettings/AI-Game-Developer-Config.json') -Raw | ConvertFrom-Json
$uri = $config.host.TrimEnd('/') + '/mcp'
$headers = @{ Accept = 'application/json, text/event-stream' }
$init = @{jsonrpc='2.0';id=1;method='initialize';params=@{protocolVersion='2024-11-05';capabilities=@{};clientInfo=@{name='FirepowerLoop-pickup-acceptance';version='1.0'}}} | ConvertTo-Json -Depth 30
$response = Invoke-WebRequest -Uri $uri -Method Post -ContentType 'application/json' -Headers $headers -Body $init -TimeoutSec 10
$headers['Mcp-Session-Id'] = [string]$response.Headers['Mcp-Session-Id'][0]
$method = if ($Tool) { 'tools/call' } else { 'tools/list' }
$paramsObject = if ($Tool) { @{ name=$Tool; arguments=$argumentsObject } } else { @{} }
$body = @{jsonrpc='2.0';id=2;method=$method;params=$paramsObject} | ConvertTo-Json -Depth 40
$response = Invoke-WebRequest -Uri $uri -Method Post -ContentType 'application/json' -Headers $headers -Body $body -TimeoutSec 55
$data = if ($response.Content.TrimStart().StartsWith('{')) { $response.Content } else {
    ($response.Content -split "`n" | Where-Object { $_.StartsWith('data: ') } | Select-Object -Last 1).Substring(6)
}
if ($OutputFile) { [IO.File]::WriteAllText([IO.Path]::GetFullPath($OutputFile), $data) }
$parsed = $data | ConvertFrom-Json -Depth 100
if (!$Tool) {
    $parsed.result.tools | Where-Object name -In @('script-execute','tests-run','screenshot-isolated','scene-open') | Select-Object name,inputSchema | ConvertTo-Json -Depth 50
} elseif ($parsed.result.structuredContent) {
    $parsed.result.structuredContent | ConvertTo-Json -Depth 100
} else { $parsed | ConvertTo-Json -Depth 100 }
if ($parsed.error -or $parsed.result.isError) { throw 'Unity MCP tool returned an error; inspect saved response' }
if ($Tool -eq 'tests-run') {
    $results = @($parsed.result.structuredContent.result.Results)
    if (!$results.Count -or $results.Count -gt $MaxTests) { throw 'Empty result or scope exceeds maximum; refusing fallback' }
    if ($results | Where-Object { $_.Name -notlike "*.$fixture.*" }) { throw 'Unexpected test scope' }
}
