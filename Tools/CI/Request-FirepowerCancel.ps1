[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$RequestFile)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not [IO.Path]::IsPathRooted($RequestFile)) { throw 'RequestFile must be absolute.' }
$request = Get-Content -LiteralPath $RequestFile -Raw | ConvertFrom-Json
if ($request.ProjectId -ne 'FirepowerLoop' -or -not [IO.Path]::IsPathRooted($request.CancellationFile)) { throw 'Invalid FirepowerLoop request.' }
$expected = Join-Path ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($RequestFile))) 'cancel.request'
if ([IO.Path]::GetFullPath($request.CancellationFile) -ine $expected) { throw 'Cancellation signal must belong to the selected request directory.' }
for ($part = [IO.Path]::GetFullPath($expected); $part; $part = [IO.Path]::GetDirectoryName($part)) {
    if ((Test-Path -LiteralPath $part) -and ((Get-Item -LiteralPath $part -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Cancellation path cannot use links.' }
}
[IO.File]::WriteAllText($expected, ('cancel requested at ' + [DateTime]::UtcNow.ToString('O')), (New-Object Text.UTF8Encoding($false)))
Write-Output 'Cancellation requested. Unity stops at the next safe checkpoint; no process is killed.'
