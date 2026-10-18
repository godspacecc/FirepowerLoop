param(
    [string]$Source = "$PSScriptRoot/Localization/settings-visuals.json",
    [string]$Destination = "$PSScriptRoot/../Client/Assets/Game/Config/Localization"
)
$ErrorActionPreference = 'Stop'
# 此文件仅发布 Config/Localization 中声明的设置页文案；其它历史文案原样保留。
$entries = Get-Content -LiteralPath $Source -Raw -Encoding UTF8 | ConvertFrom-Json
$pending = @()
foreach ($language in @('zh-CN', 'en-US')) {
    $path = Join-Path $Destination ($language + '.json')
    $content = [IO.File]::ReadAllText($path)
    $newline = if ($content.Contains("`r`n")) { "`r`n" } else { "`n" }
    foreach ($entry in $entries.PSObject.Properties) {
        $value = $entry.Value.$language
        if ([string]::IsNullOrWhiteSpace($value)) { throw "Missing $language translation: $($entry.Name)" }
        $keyJson = ConvertTo-Json -InputObject $entry.Name -Compress
        $valueJson = ConvertTo-Json -InputObject $value -Compress
        $line = '  ' + $keyJson + ': ' + $valueJson
        $pattern = '(?m)^[ \t]*' + [regex]::Escape($keyJson) + '[ \t]*:[ \t]*"(?:\\.|[^"\\])*"(?<comma>,?)[ \t]*(?=\r?$)'
        $match = [regex]::Match($content, $pattern)
        if ($match.Success) {
            $content = $content.Substring(0, $match.Index) + $line + $match.Groups['comma'].Value + $content.Substring($match.Index + $match.Length)
        } else {
            $end = $content.LastIndexOf('}')
            if ($end -lt 0) { throw "Invalid localization file: $path" }
            $content = $content.Substring(0, $end).TrimEnd() + ',' + $newline + $line + $newline + '}' + $newline
        }
    }
    $null = ConvertFrom-Json -InputObject $content
    $pending += @{ Path = $path; Content = $content }
}
foreach ($item in $pending) {
    if ([IO.File]::ReadAllText($item.Path) -ne $item.Content) {
        [IO.File]::WriteAllText($item.Path, $item.Content, (New-Object Text.UTF8Encoding($false)))
    }
}
Write-Output 'Settings localization published: zh-CN, en-US.'
