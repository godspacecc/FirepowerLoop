$ErrorActionPreference = 'Stop'
Add-Type -Path 'C:/Program Files/dotnet/sdk/8.0.422/Roslyn/bincore/Microsoft.CodeAnalysis.dll'
Add-Type -Path 'C:/Program Files/dotnet/sdk/8.0.422/Roslyn/bincore/Microsoft.CodeAnalysis.CSharp.dll'
$options = [Microsoft.CodeAnalysis.CSharp.CSharpParseOptions]::Default.WithLanguageVersion([Microsoft.CodeAnalysis.CSharp.LanguageVersion]::CSharp9).WithPreprocessorSymbols([string[]]@('UNITY_EDITOR','UNITY_INCLUDE_TESTS'))
$files = @('Client/Assets/Editor/Build/AndroidBuildUtility.cs','Client/Assets/Editor/YooAsset/ResourceServerPublishUtility.cs')
$errors = @()
foreach ($file in $files) {
 $tree = [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText((Join-Path (Get-Location) $file)), $options, $file, [Text.Encoding]::UTF8, [Threading.CancellationToken]::None)
 $errors += @($tree.GetDiagnostics() | Where-Object Severity -eq Error | ForEach-Object { $_.ToString() })
}
@{CheckedFiles=$files;SyntaxErrors=$errors;UnityCompilationRun=$false;UnityTestsRun=$false} | ConvertTo-Json -Depth 4 | Set-Content -Encoding utf8 'Backup/Artifacts/AndroidResourceServerUpdate-20261030/syntax-check.json'
if ($errors.Count) { throw ($errors -join "`n") }
Write-Output 'C#9 syntax parsing passed for the changed file; no Unity compilation.'
