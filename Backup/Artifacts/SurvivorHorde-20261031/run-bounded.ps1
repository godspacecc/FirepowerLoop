param([string[]]$Methods=@('HordeExpansionUsesFrameBudgetWithoutLosingPopulation','HordeWakeFailureDoesNotConsumeGroup','HordePartialWakeFailureCommitsEarlierTransfers','HordeRetirementAndReturnPreserveDamagedIdentity','HordePausedFrameDoesNotAdvanceOrExpand','HordeStopClearsBothSimulationLayers'))
$ErrorActionPreference='Stop'
foreach($method in $Methods){
    $argsFile=Join-Path $PSScriptRoot ($method+'-args.json')
    $resultFile=Join-Path $PSScriptRoot ($method+'-result.json')
    @{testMode='EditMode';testMethod="Game.Tests.SurvivorRuntimeTests.$method";includePassingTests=$true} | ConvertTo-Json | Set-Content -LiteralPath $argsFile
    & "$PSScriptRoot/../../../Tools/PickupFeedbackChecks/Invoke-UnityMcp.ps1" -Tool tests-run -ArgumentsFile $argsFile -OutputFile $resultFile -MaxTests 1 | Out-Null
    $data=Get-Content -LiteralPath $resultFile -Raw | ConvertFrom-Json -Depth 100
    $summary=$data.result.structuredContent.result.Summary
    if(!$summary -or $summary.TotalTests -ne 1 -or $summary.FailedTests -ne 0 -or $summary.PassedTests -ne 1){throw "Failed or invalid result: $method; see $resultFile"}
    Write-Output "PASS $method (1/1)"
}
