param([ValidateSet('BakeAll', 'Validate', 'CaptureApplied')][string]$Action = 'Validate')
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Push-Location $taskRoot
try {
    # 与已采用的候选原文件同次编译；烘焙脚本只负责持久化，不复制另一套视觉设计。
    $previewSource = Get-Content -LiteralPath 'Tools/Unity/NextThreeStagesArtPreview.cs' -Raw
    $bakeSource = Get-Content -LiteralPath 'Tools/Unity/NextThreeStagesAssetBake.cs' -Raw
    $bakeSource = [regex]::Replace($bakeSource, '(?m)^using [^\r\n]+\r?\n', '')
    $argumentsPath = Join-Path $taskRoot 'Client/Temp/next-three-assets-command.json'
    $outputPath = Join-Path $taskRoot ('Client/Temp/next-three-assets-' + $Action + '-result.json')
    @{ csharpCode = $previewSource + "`n" + $bakeSource; className = 'NextThreeStagesAssetBake'; methodName = $Action; isMethodBody = $false } |
        ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $argumentsPath
    & 'Tools/PickupFeedbackChecks/Invoke-UnityMcp.ps1' -Tool script-execute -ArgumentsFile $argumentsPath -OutputFile $outputPath
}
finally { Pop-Location }
