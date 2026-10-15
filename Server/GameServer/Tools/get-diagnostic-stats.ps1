param(
    [string]$StoreRoot = (Join-Path (Split-Path $PSScriptRoot -Parent) 'Store/Diagnostics')
)

$ErrorActionPreference = 'Stop'
$groups = @{}
$known = @{}
if (-not (Test-Path -LiteralPath $StoreRoot -PathType Container)) { return }
foreach ($file in Get-ChildItem -LiteralPath $StoreRoot -Filter '*.jsonl' -File | Sort-Object Name) {
    foreach ($line in [System.IO.File]::ReadLines($file.FullName)) {
        if ([string]::IsNullOrWhiteSpace($line)) { throw "空诊断行：$($file.FullName)" }
        $batch = $line | ConvertFrom-Json -Depth 12
        if ($null -eq $batch.BatchId -or $null -eq $batch.Reports) { throw "无效诊断行：$($file.FullName)" }
        if ($known.ContainsKey([string]$batch.BatchId)) { continue }
        $known[[string]$batch.BatchId] = $true
        foreach ($report in $batch.Reports) {
            $parts = @($report.EventDate, $report.BuildVersion, $report.Platform, $report.Source, $report.Kind, $report.Fingerprint)
            if ($parts | Where-Object { [string]::IsNullOrWhiteSpace([string]$_) }) { throw "无效统计字段：$($file.FullName)" }
            $key = $parts -join "`t"
            if (-not $groups.ContainsKey($key)) { $groups[$key] = [long]0 }
            $groups[$key] += [long]$report.Count
        }
    }
}
foreach ($entry in $groups.GetEnumerator() | Sort-Object Key) {
    $parts = $entry.Key.Split("`t")
    [pscustomobject]@{
        EventDate = $parts[0]
        BuildVersion = $parts[1]
        Platform = $parts[2]
        Source = $parts[3]
        Kind = $parts[4]
        Fingerprint = $parts[5]
        Count = $entry.Value
    }
}
