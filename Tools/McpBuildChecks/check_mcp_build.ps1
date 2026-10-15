param(
    [Parameter(Mandatory = $true)][string]$SourcePath,
    [Parameter(Mandatory = $true)][ValidateSet('McpLinkExtractorRegressionTests')][string]$TestClass,
    [Parameter(Mandatory = $true)][ValidateRange(3, 3)][int]$MaxTests,
    [Parameter(Mandatory = $true)][switch]$IncludePassingTests
)

$ErrorActionPreference = 'Stop'
if (!$IncludePassingTests) { throw '必须输出通过用例。' }
$source = (Resolve-Path -LiteralPath $SourcePath).Path
Add-Type -Path @($source, (Join-Path $PSScriptRoot 'McpLinkExtractorRegressionTests.cs'))
$failed = [McpLinkExtractorRegressionTests]::Run()
if ($failed -ne 0) { exit 1 }
