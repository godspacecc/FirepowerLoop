$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath('E:\GitHub\FirepowerLoop')
$reportDir = Join-Path $workspace 'Backup\Artifacts\LocalArtifactCleanup-20261030'
Set-Location -LiteralPath $workspace
$manifest = Get-Content -LiteralPath (Join-Path $reportDir 'deletion-manifest.json') -Raw | ConvertFrom-Json
$tracked = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
git -c core.quotepath=false ls-files | ForEach-Object { [void]$tracked.Add($_) }
if ($LASTEXITCODE -ne 0) { throw 'Cannot query tracked paths' }
$processes = @(Get-CimInstance Win32_Process | Where-Object { $_.Name -match '^(dotnet|testhost|ffmpeg|MSBuild)(\.exe)?$' })
$validated = @()
foreach ($item in $manifest) {
    $absolute = [IO.Path]::GetFullPath((Join-Path $workspace $item.path))
    if (!$absolute.StartsWith($workspace + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Outside workspace: $absolute" }
    if ($item.path -notmatch '^(Backup/Artifacts/|Server/AccountFoundation/(bin|obj)$|Tools/Arcade[^/]+/(bin|obj)$)') { throw "Unexpected scope: $absolute" }
    $target = Get-Item -LiteralPath $absolute
    $entries = @($target)
    if ($target.PSIsContainer) { $entries += @(Get-ChildItem -LiteralPath $absolute -Recurse -Force) }
    if (@($entries | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) { throw "Reparse point: $absolute" }
    $files = @($entries | Where-Object { !$_.PSIsContainer })
    $total = ($files | Measure-Object -Property Length -Sum).Sum
    if ($files.Count -ne $item.files -or $total -ne $item.bytes) { throw "Target changed since inventory: $absolute" }
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($workspace.Length + 1).Replace('\','/')
        if ($tracked.Contains($relative)) { throw "Tracked file cannot be deleted: $relative" }
    }
    foreach ($process in $processes) {
        $command = [string]$process.CommandLine
        if ($command.Replace('/','\').IndexOf($absolute,[StringComparison]::OrdinalIgnoreCase) -ge 0) { throw "Target is in use by process $($process.ProcessId): $absolute" }
    }
    $validated += [pscustomobject]@{ path=$item.path; absolute=$absolute; directory=$target.PSIsContainer; files=$item.files; bytes=$item.bytes }
}
# Preserve hashes for tracked evidence and the three outstanding formal-file differences.
$preserved = @()
foreach ($relative in $tracked) {
    if ($relative -match '^Backup/Artifacts/(DockThreeSideApplied-20261030|DockThreeSideEntry-20261030|EnemyOutsideEntry-20261030|AccountFoundation-20261030|NetworkAlignment-20261030)' -or $relative -in @('Client/Assets/Game/Art/Arcade/Island/LowPolyV2/IslandLowPolyV2.asset','Client/Assets/Game/Fonts/Chinese/SourceHanSansCN-Normal SDF.asset','Client/Assets/Game/Dlls/HybridCLRAssemblyList.bytes')) {
        $file = Join-Path $workspace $relative
        if (Test-Path -LiteralPath $file -PathType Leaf) { $preserved += [pscustomobject]@{ path=$relative; hash=(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash } }
    }
}
$preserved | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $reportDir 'preserved-hashes.json') -Encoding utf8
foreach ($item in $validated) {
    if ($item.directory) { Remove-Item -LiteralPath $item.absolute -Recurse -Force }
    else { Remove-Item -LiteralPath $item.absolute -Force }
}
foreach ($item in $validated) { if (Test-Path -LiteralPath $item.absolute) { throw "Deletion incomplete: $($item.path)" } }
foreach ($item in $preserved) {
    if ((Get-FileHash -LiteralPath (Join-Path $workspace $item.path) -Algorithm SHA256).Hash -ne $item.hash) { throw "Preserved evidence changed: $($item.path)" }
}
$result = [pscustomobject]@{ targets=$validated.Count; files=($validated|Measure-Object files -Sum).Sum; bytes=($validated|Measure-Object bytes -Sum).Sum; preservedFiles=$preserved.Count; preservedHashesUnchanged=$true }
$result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $reportDir 'result.json') -Encoding utf8
$result | ConvertTo-Json -Compress
