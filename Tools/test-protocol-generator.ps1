$ErrorActionPreference = "Stop"

$ToolsRoot = Split-Path -Parent $PSCommandPath
$Root = Split-Path -Parent $ToolsRoot
$Project = Join-Path $Root "Tools/ProtocolGenerator/ProtocolGenerator.csproj"
$Source = Join-Path $Root "Config/Proto"
$WorkspaceOutputs = @{
    Client = Join-Path $Root "Client/Assets/Game/Scripts/Network/Generated"
    Server = Join-Path $Root "Server/GameServer/Runtime/Model/Generated/Message"
}

$TempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("FirepowerLoop.ProtocolGenerator.Test." + [System.Guid]::NewGuid().ToString("N"))

function Get-TopLevelCsHashes {
    param([string]$Directory)

    $hashes = @{}
    if (Test-Path -LiteralPath $Directory) {
        Get-ChildItem -LiteralPath $Directory -File -Filter "*.cs" |
            Sort-Object Name |
            ForEach-Object {
                $hashes[$_.Name] = (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash
            }
    }
    return $hashes
}

function Assert-HashSetEqual {
    param(
        [hashtable]$Expected,
        [hashtable]$Actual,
        [string]$Name
    )

    $expectedNames = @($Expected.Keys | Sort-Object)
    $actualNames = @($Actual.Keys | Sort-Object)
    if (($expectedNames -join "`n") -ne ($actualNames -join "`n")) {
        throw "$Name file set mismatch."
    }

    foreach ($fileName in $expectedNames) {
        if ($Expected[$fileName] -ne $Actual[$fileName]) {
            throw "$Name hash mismatch: $fileName"
        }
    }
}

function Write-HashComparison {
    param(
        [hashtable]$Expected,
        [hashtable]$Actual,
        [string]$Name,
        [string]$ExpectedLabel,
        [string]$ActualLabel
    )

    Write-Host "===== $Name SHA-256 comparison ====="
    Write-Host "FileName`t$ExpectedLabel`t$ActualLabel`tMatch"
    $allNames = @($Expected.Keys + $Actual.Keys | Sort-Object -Unique)
    foreach ($fileName in $allNames) {
        $expectedHash = if ($Expected.ContainsKey($fileName)) { $Expected[$fileName] } else { "<missing>" }
        $actualHash = if ($Actual.ContainsKey($fileName)) { $Actual[$fileName] } else { "<missing>" }
        $match = $expectedHash -eq $actualHash
        Write-Host "$fileName`t$expectedHash`t$actualHash`t$match"
    }
}

function Invoke-Generator {
    param(
        [string]$Client,
        [string]$Server
    )

    & dotnet run --project $Project -- `
        --source $Source `
        --client-output $Client `
        --server-output $Server

    if ($LASTEXITCODE -ne 0) {
        throw "Protocol generator failed with exit code $LASTEXITCODE."
    }
}

function Assert-GeneratorFails {
    param(
        [string[]]$Arguments,
        [string]$Name
    )

    & dotnet run --project $Project -- @Arguments
    if ($LASTEXITCODE -eq 0) {
        throw "Expected generator failure did not occur: $Name"
    }
}

try {
    New-Item -ItemType Directory -Force -Path $TempRoot | Out-Null

    $baseline = @{}
    foreach ($key in $WorkspaceOutputs.Keys) {
        $baseline[$key] = Get-TopLevelCsHashes $WorkspaceOutputs[$key]
    }

    $first = @{
        Client = Join-Path $TempRoot "First/Client"
        Server = Join-Path $TempRoot "First/Server"
    }

    Invoke-Generator $first.Client $first.Server
    foreach ($key in $WorkspaceOutputs.Keys) {
        $generatedHashes = Get-TopLevelCsHashes $first[$key]
        Assert-HashSetEqual $baseline[$key] $generatedHashes $key
        Write-HashComparison $baseline[$key] $generatedHashes $key "WorkspaceSHA256" "GeneratedSHA256"
    }

    $firstHashes = @{
        Client = Get-TopLevelCsHashes $first.Client
        Server = Get-TopLevelCsHashes $first.Server
    }

    Invoke-Generator $first.Client $first.Server
    foreach ($key in $WorkspaceOutputs.Keys) {
        Assert-HashSetEqual $firstHashes[$key] (Get-TopLevelCsHashes $first[$key]) "$key idempotency"
    }

    $badOutput = Join-Path $TempRoot "BadSameOutput"
    Assert-GeneratorFails @("--unknown", "value") "unknown argument"
    Assert-GeneratorFails @("--source", (Join-Path $TempRoot "MissingSource"), "--client-output", (Join-Path $TempRoot "BadClient"), "--server-output", (Join-Path $TempRoot "BadServer")) "missing source"
    Assert-GeneratorFails @("--source", $Source, "--client-output", $badOutput, "--server-output", $badOutput) "duplicate output directories"

    # Preserve explicit gaps, continue implicit numbering, and reject invalid IDs before writing.
    $fixtureSource = Join-Path $TempRoot "FixtureSource"
    New-Item -ItemType Directory -Force -Path $fixtureSource | Out-Null
    $fixtureProto = Join-Path $fixtureSource "Fixture_C_100.proto"
    $fixtureClient = Join-Path $TempRoot "FixtureClient"
    $fixtureServer = Join-Path $TempRoot "FixtureServer"
    $fixtureArguments = @("--source", $fixtureSource, "--client-output", $fixtureClient, "--server-output", $fixtureServer)
    @"
// Opcode 110
message First
{
    int32 Value = 1;
}
// Opcode 120
message Second
{
    int32 Value = 1;
}
message Third
{
    int32 Value = 1;
}
"@ | Set-Content -LiteralPath $fixtureProto -Encoding UTF8
    & dotnet run --project $Project -- @fixtureArguments
    if ($LASTEXITCODE -ne 0) { throw "Explicit opcode fixture generation failed." }
    $fixtureGenerated = Get-Content -LiteralPath (Join-Path $fixtureClient "Fixture_C_100.cs") -Raw
    foreach ($expected in @("First = 110;", "Second = 120;", "Third = 121;")) {
        if (!$fixtureGenerated.Contains($expected)) { throw "Missing stable opcode: $expected" }
    }
    $fixtureHashes = Get-TopLevelCsHashes $fixtureClient
    foreach ($declaration in @("// Opcode 110", "// Opcode 65536", "// Opcode 130`n// Opcode 131")) {
        @"
// Opcode 110
message First
{
}
$declaration
message Second
{
}
"@ | Set-Content -LiteralPath $fixtureProto -Encoding UTF8
        Assert-GeneratorFails $fixtureArguments "invalid explicit opcode: $declaration"
        Assert-HashSetEqual $fixtureHashes (Get-TopLevelCsHashes $fixtureClient) "invalid opcode preserves outputs"
    }

    foreach ($key in $WorkspaceOutputs.Keys) {
        Assert-HashSetEqual $baseline[$key] (Get-TopLevelCsHashes $WorkspaceOutputs[$key]) "$key workspace unchanged"
    }

    Write-Host "Protocol generator test passed."
    exit 0
}
finally {
    if (Test-Path -LiteralPath $TempRoot) {
        $resolvedTemp = [System.IO.Path]::GetFullPath($TempRoot)
        $expectedTempParent = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd('\') + '\'
        if (!$resolvedTemp.StartsWith($expectedTempParent, [System.StringComparison]::OrdinalIgnoreCase) -or
            !(Split-Path -Leaf $resolvedTemp).StartsWith("FirepowerLoop.ProtocolGenerator.Test.")) {
            throw "Refusing to remove unexpected test directory: $resolvedTemp"
        }
        Remove-Item -LiteralPath $TempRoot -Recurse -Force
    }
}
