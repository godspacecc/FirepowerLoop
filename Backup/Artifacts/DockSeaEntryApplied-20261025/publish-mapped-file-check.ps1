# 真实映射文件复现与发布验证，仅使用本批次隔离目录，不碰Unity资源或服务器数据。
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskRoot = Split-Path -Parent $taskRoot
$taskRoot = Split-Path -Parent $taskRoot
$fixture = Join-Path $PSScriptRoot 'publisher-fixture'
$sourceDirectory = Join-Path $fixture 'source'
$targetDirectory = Join-Path $fixture 'target'
New-Item -ItemType Directory -Path $sourceDirectory,$targetDirectory -Force | Out-Null
$sourceFile = Join-Path $sourceDirectory 'fixture.bytes'
$targetFile = Join-Path $targetDirectory 'fixture.bytes'
$metaFile = $targetFile + '.meta'
[IO.File]::WriteAllBytes($sourceFile,[byte[]](4,5,6))
[IO.File]::WriteAllBytes($targetFile,[byte[]](1,2,3))
[IO.File]::WriteAllText($metaFile,'fixed-fixture-meta')
$stream = $null; $mapping = $null; $view = $null
try {
    $stream = [IO.File]::Open($targetFile,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
    $mapping = [IO.MemoryMappedFiles.MemoryMappedFile]::CreateFromFile($stream,[System.Management.Automation.Language.NullString]::Value,0,[IO.MemoryMappedFiles.MemoryMappedFileAccess]::Read,[IO.HandleInheritability]::None,$true)
    $view = $mapping.CreateViewAccessor(0,0,[IO.MemoryMappedFiles.MemoryMappedFileAccess]::Read)
    $blocked = $false
    try { Copy-Item -LiteralPath $sourceFile -Destination $targetFile -ErrorAction Stop }
    catch { $blocked = $true; Write-Output ('Expected old COPY failure: ' + $_.Exception.Message) }
    if (-not $blocked) { throw '未复现真实映射文件截断失败，不能计为验证通过' }
    & (Join-Path $taskRoot 'Config/publish-generated.ps1') -Source $sourceDirectory -Destination $targetDirectory -Pattern '*.bytes'
    if ([BitConverter]::ToString([IO.File]::ReadAllBytes($targetFile)) -ne '04-05-06') { throw '原子发布没有更新路径内容' }
    if ($view.ReadByte(0) -ne 1) { throw '旧映射视图不应因替换而改变' }
    if ([IO.File]::ReadAllText($metaFile) -ne 'fixed-fixture-meta') { throw 'meta被修改' }
    $publishedTime = [IO.File]::GetLastWriteTimeUtc($targetFile)
    & (Join-Path $taskRoot 'Config/publish-generated.ps1') -Source $sourceDirectory -Destination $targetDirectory -Pattern '*.bytes'
    if ([IO.File]::GetLastWriteTimeUtc($targetFile) -ne $publishedTime) { throw '相同内容没有跳过' }
    if ((Get-ChildItem -LiteralPath $targetDirectory -Filter '.luban-*.tmp').Count -ne 0) { throw '发布残留临时文件' }
    Write-Output 'PASS mapped bytes atomic publication; old mapping intact; target updated; meta preserved; identical hash skipped; no temporary residue'
}
finally {
    if ($view) { $view.Dispose() }; if ($mapping) { $mapping.Dispose() }; if ($stream) { $stream.Dispose() }
}
