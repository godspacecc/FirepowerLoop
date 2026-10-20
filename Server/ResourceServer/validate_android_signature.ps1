param(
    [Parameter(Mandatory = $true)][string]$InputFile,
    [ValidateSet('Apk', 'Keystore')][string]$Mode = 'Apk',
    [string]$KeyAlias,
    [string]$Policy,
    [string]$UnityExe = $env:FIREPOWER_UNITY_EXE,
    [string]$JavaPath,
    [string]$AndroidSdk = $env:ANDROID_SDK_ROOT
)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
Set-StrictMode -Version Latest
if (-not $Policy) { $Policy = Join-Path $PSScriptRoot 'android-signing.json' }

# 构建与两种发布入口共用证书白名单；不打印密码，不改 APK，不自动生成密钥。
$identity = Get-Content -LiteralPath $Policy -Raw -Encoding UTF8 | ConvertFrom-Json
if ($identity.schemaVersion -ne 1 -or $identity.certificateSha256 -cnotmatch '^[0-9a-f]{64}$') {
    throw 'Invalid Android signing policy.'
}
if (-not (Test-Path -LiteralPath $InputFile -PathType Leaf)) { throw 'Signing input file is missing.' }
if ($UnityExe) {
    $androidPlayer = Join-Path (Split-Path $UnityExe -Parent) 'Data/PlaybackEngines/AndroidPlayer'
    if (-not $JavaPath) { $JavaPath = Join-Path $androidPlayer 'OpenJDK/bin/java.exe' }
    if (-not $AndroidSdk) { $AndroidSdk = Join-Path $androidPlayer 'SDK' }
}
if (-not $JavaPath -or -not (Test-Path -LiteralPath $JavaPath -PathType Leaf)) {
    throw 'Set FIREPOWER_UNITY_EXE or supply JavaPath for signature validation.'
}

function Invoke-SigningTool([string]$Executable, [string[]]$ToolArguments) {
    # 工具密码通过子进程环境读取，不能拼到命令行或异常里。
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $lines = @(& $Executable @ToolArguments 2>&1)
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = $previousPreference }
    if ($code -ne 0) { throw "Android $Mode validation tool failed (exit $code). Check input, alias and credentials." }
    return ($lines -join "`n")
}

if ($Mode -eq 'Keystore') {
    if (-not $KeyAlias -or -not $env:FIREPOWER_SIGNING_STORE_PASSWORD -or -not $env:FIREPOWER_SIGNING_KEY_PASSWORD) {
        throw 'Keystore alias or signing credentials are missing.'
    }
    $keytool = Join-Path (Split-Path $JavaPath -Parent) 'keytool.exe'
    $arguments = @('-exportcert', '-rfc', '-keystore', $InputFile, '-alias', $KeyAlias,
        '-storepass:env', 'FIREPOWER_SIGNING_STORE_PASSWORD')
    $certificate = Invoke-SigningTool $keytool $arguments
    $match = [regex]::Match($certificate, '(?s)-----BEGIN CERTIFICATE-----(.*?)-----END CERTIFICATE-----')
    if (-not $match.Success) { throw 'Cannot read signing certificate.' }
    $hash = [Security.Cryptography.SHA256]::Create()
    try { $fingerprints = @([BitConverter]::ToString($hash.ComputeHash([Convert]::FromBase64String($match.Groups[1].Value))).Replace('-', '').ToLowerInvariant()) }
    finally { $hash.Dispose() }
    # 请求签名会实际解锁私钥，提前发现错误的 keyPassword 或仅包含公钥的别名。
    $requestFile = Join-Path ([IO.Path]::GetTempPath()) ('firepower-signing-' + [Guid]::NewGuid().ToString('N') + '.csr')
    try {
        $null = Invoke-SigningTool $keytool @('-certreq', '-keystore', $InputFile, '-alias', $KeyAlias,
            '-storepass:env', 'FIREPOWER_SIGNING_STORE_PASSWORD', '-keypass:env', 'FIREPOWER_SIGNING_KEY_PASSWORD', '-file', $requestFile)
    } finally { if (Test-Path -LiteralPath $requestFile) { Remove-Item -LiteralPath $requestFile -Force } }
} else {
    if (-not $AndroidSdk) { throw 'Supply AndroidSdk or FIREPOWER_UNITY_EXE for APK validation.' }
    $buildTools = Join-Path $AndroidSdk 'build-tools'
    $signer = Get-ChildItem -LiteralPath $buildTools -Directory |
        Where-Object { $_.Name -match '^\d+\.\d+\.\d+$' } |
        Sort-Object { [version]$_.Name } -Descending |
        ForEach-Object { Join-Path $_.FullName 'lib/apksigner.jar' } |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    if (-not $signer) { throw 'Android SDK apksigner.jar is missing.' }
    $output = Invoke-SigningTool $JavaPath @('-jar', $signer, 'verify', '--print-certs', $InputFile)
    $fingerprints = @([regex]::Matches($output, '(?m)^Signer #\d+ certificate SHA-256 digest: ([0-9a-fA-F]{64})\s*$') |
        ForEach-Object { $_.Groups[1].Value.ToLowerInvariant() })
}
if ($fingerprints.Count -ne 1 -or $fingerprints[0] -cne $identity.certificateSha256) {
    throw "SIGNATURE_MISMATCH: expected $($identity.certificateSha256); actual $($fingerprints -join ',')."
}
Write-Output ('Verified Android signing certificate: ' + $identity.certificateSha256)
