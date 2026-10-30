$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath('E:\GitHub\FirepowerLoop')
$batch = Join-Path $workspace 'Backup\Artifacts\LocalArtifactCleanup-20261030'
Set-Location -LiteralPath $workspace
$items = Get-Content -LiteralPath (Join-Path $batch 'second-batch-manifest.json') -Raw | ConvertFrom-Json
$kept = Get-Content -LiteralPath (Join-Path $batch 'second-batch-preserved.json') -Raw | ConvertFrom-Json
$tracked = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
git -c core.quotepath=false ls-files | ForEach-Object { [void]$tracked.Add($_) }
if ($LASTEXITCODE -ne 0) { throw 'Git listing failed' }
$targets = @()
foreach ($item in $items) {
    $path = [IO.Path]::GetFullPath((Join-Path $workspace $item.path))
    if (!$path.StartsWith((Join-Path $workspace 'Backup\Artifacts\'), [StringComparison]::OrdinalIgnoreCase)) { throw "Outside artifacts: $path" }
    if ($item.path -notmatch '^Backup/Artifacts/(DockThreeSideApplied-20261030|EnemyOutsideEntry-20261030)/[^/]+$') { throw "Unexpected target: $path" }
    if ($tracked.Contains($item.path)) { throw "Tracked file: $path" }
    $file = Get-Item -LiteralPath $path
    if ($file.PSIsContainer -or ($file.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Not an ordinary file: $path" }
    if ($file.Length -ne $item.bytes -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $item.sha256) { throw "File changed: $path" }
    $targets += $path
}
foreach ($item in $kept) {
    if ((Get-FileHash -LiteralPath (Join-Path $workspace $item.path) -Algorithm SHA256).Hash -ne $item.sha256) { throw "Preserved file changed before cleanup: $($item.path)" }
}
foreach ($path in $targets) { Remove-Item -LiteralPath $path -Force }
foreach ($path in $targets) { if (Test-Path -LiteralPath $path) { throw "Deletion incomplete: $path" } }
foreach ($item in $kept) {
    if ((Get-FileHash -LiteralPath (Join-Path $workspace $item.path) -Algorithm SHA256).Hash -ne $item.sha256) { throw "Preserved file changed: $($item.path)" }
}
$result = [pscustomobject]@{ files=$items.Count; bytes=($items|Measure-Object bytes -Sum).Sum; preservedFiles=$kept.Count; preservedHashesUnchanged=$true }
$result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $batch 'second-batch-result.json') -Encoding utf8
$result | ConvertTo-Json -Compress
