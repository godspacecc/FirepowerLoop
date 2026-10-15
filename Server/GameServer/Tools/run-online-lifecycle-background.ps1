param(
    [Parameter(Mandatory = $true)]
    [string]$LogPath,
    [Parameter(Mandatory = $true)]
    [string]$ExitCodePath
)

$ErrorActionPreference = "Continue"
$utf8 = [System.Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = $utf8
$OutputEncoding = $utf8

Set-Location (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
New-Item -ItemType Directory -Force -Path ([System.IO.Path]::GetDirectoryName($LogPath)) | Out-Null

[System.IO.File]::WriteAllText($LogPath, "===== BACKGROUND RUN START $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz') =====`n", $utf8)

try {
    & powershell -NoProfile -ExecutionPolicy Bypass -File "Server\GameServer\Tools\run-online-lifecycle-tests.ps1" 2>&1 |
        ForEach-Object {
            [System.IO.File]::AppendAllText($LogPath, "$_`n", $utf8)
        }
    $code = $LASTEXITCODE
} catch {
    [System.IO.File]::AppendAllText($LogPath, "RUNNER_ERROR=$($_.Exception.Message)`n", $utf8)
    $code = 999
}

[System.IO.File]::AppendAllText($LogPath, "===== BACKGROUND RUN END $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz') EXIT_CODE=$code =====`n", $utf8)
[System.IO.File]::WriteAllText($ExitCodePath, "$code", [System.Text.Encoding]::ASCII)
exit $code
