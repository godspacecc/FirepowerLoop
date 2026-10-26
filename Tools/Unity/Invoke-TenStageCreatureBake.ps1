param([switch]$Readback,[switch]$Capture)
$ErrorActionPreference='Stop'
$creatureRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$creatureFolder=Join-Path $creatureRoot 'Backup/Artifacts/TenStageCreaturesApplied-20261026'
$creatureUsings=[Collections.Generic.List[string]]::new()
$creatureBodies=[Collections.Generic.List[string]]::new()
$creatureClass=if($Capture){'TenStageCreatureAppliedCapture'}elseif($Readback){'TenStageCreatureReadback'}else{'TenStageCreatureBake'}
$creatureFiles=@('TenStageCreatureCandidates.cs','TenStageCreatureBake.cs')
if($creatureClass -ne 'TenStageCreatureBake'){$creatureFiles+=($creatureClass+'.cs')}
foreach($creatureFile in $creatureFiles) {
    foreach($creatureLine in (Get-Content -LiteralPath (Join-Path $PSScriptRoot $creatureFile))) {
        if($creatureLine.StartsWith('using ')) { if(!$creatureUsings.Contains($creatureLine)){$creatureUsings.Add($creatureLine)} }
        else {$creatureBodies.Add($creatureLine)}
    }
}
$creatureCode=($creatureUsings -join "`n")+"`n"+($creatureBodies -join "`n")
$creatureMode=if($Capture){'capture'}elseif($Readback){'readback'}else{'bake'}
$creatureInput=Join-Path $creatureFolder ($creatureMode+'-input.json')
@{csharpCode=$creatureCode;className=$creatureClass;methodName='Run'} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $creatureInput
& (Join-Path $creatureRoot 'Tools/PickupFeedbackChecks/Invoke-UnityMcp.ps1') -Tool script-execute -ArgumentsFile $creatureInput -OutputFile (Join-Path $creatureFolder ($creatureMode+'-result.json'))
