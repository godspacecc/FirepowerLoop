param([switch]$Readback)
$ErrorActionPreference='Stop'
# 复用已采用的临时模型生成器，仅由显式入口执行正式烘焙。
$dragonRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$dragonFolder=Join-Path $dragonRoot 'Backup/Artifacts/FinalDragonApplied-20261026'
$dragonUsings=[Collections.Generic.List[string]]::new()
$dragonBodies=[Collections.Generic.List[string]]::new()
$dragonClass=if($Readback){'FinalDragonReadback'}else{'FinalDragonBake'}
foreach($dragonFile in @('FinalDragonCandidate.cs',($dragonClass+'.cs'))) {
    foreach($dragonLine in (Get-Content -LiteralPath (Join-Path $PSScriptRoot $dragonFile))) {
        if($dragonLine.StartsWith('using ')) { if(!$dragonUsings.Contains($dragonLine)){$dragonUsings.Add($dragonLine)} }
        else {$dragonBodies.Add($dragonLine)}
    }
}
$dragonCode=($dragonUsings -join "`n")+"`n"+($dragonBodies -join "`n")
$dragonMode=if($Readback){'readback'}else{'bake'}
$dragonInput=Join-Path $dragonFolder ($dragonMode+'-input.json')
@{csharpCode=$dragonCode;className=$dragonClass;methodName='Run'} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $dragonInput
& (Join-Path $dragonRoot 'Tools/PickupFeedbackChecks/Invoke-UnityMcp.ps1') -Tool script-execute -ArgumentsFile $dragonInput -OutputFile (Join-Path $dragonFolder ($dragonMode+'-result.json'))
