param([switch]$Video)
$ErrorActionPreference = 'Stop'
# 合并临时模型与拍摄工具到一次Roslyn调用；不把脚本导入Unity正式资源目录。
$dragonRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$dragonFolder = Join-Path $dragonRoot 'Backup/Artifacts/FinalDragonModel-v1-20261026'
$dragonUsings = [Collections.Generic.List[string]]::new()
$dragonBodies = [Collections.Generic.List[string]]::new()
foreach ($dragonFile in @('FinalDragonCandidate.cs','FinalDragonPreviewCapture.cs')) {
    $dragonLines = Get-Content -LiteralPath (Join-Path $PSScriptRoot $dragonFile)
    foreach ($dragonLine in $dragonLines) {
        if ($dragonLine.StartsWith('using ')) { if (!$dragonUsings.Contains($dragonLine)) { $dragonUsings.Add($dragonLine) } }
        else { $dragonBodies.Add($dragonLine) }
    }
}
$dragonMode = if ($Video) { 'true' } else { 'false' }
$dragonCode = ($dragonUsings -join "`n") + "`n" + ($dragonBodies -join "`n") + "`npublic static class DragonInvoke { public static string Run() => FinalDragonPreviewCapture.Run($dragonMode); }"
$dragonInput = Join-Path $dragonFolder 'capture-input.json'
@{csharpCode=$dragonCode;className='DragonInvoke';methodName='Run'} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $dragonInput
& (Join-Path $dragonRoot 'Tools/PickupFeedbackChecks/Invoke-UnityMcp.ps1') -Tool script-execute -ArgumentsFile $dragonInput -OutputFile (Join-Path $dragonFolder 'capture-result.json')
