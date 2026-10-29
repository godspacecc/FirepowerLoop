param(
    [string]$Source = "$PSScriptRoot/Localization/settings-visuals.json",
    [string]$Destination = "$PSScriptRoot/../Client/Assets/Game/Config/Localization",
    [string]$RetiredKeysSource = "$PSScriptRoot/Localization/retired-keys.json"
)
$ErrorActionPreference = 'Stop'
# 只删除权威清单中的退役键；保留其余行顺序和格式，再发布指定源中的现役文案。
$entries = Get-Content -LiteralPath $Source -Raw -Encoding UTF8 | ConvertFrom-Json
$retiredKeys = Get-Content -LiteralPath $RetiredKeysSource -Raw -Encoding UTF8 | ConvertFrom-Json
$retiredSet = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
foreach ($key in $retiredKeys) {
    if ($key -isnot [string] -or [string]::IsNullOrWhiteSpace($key) -or !$retiredSet.Add($key)) {
        throw "Invalid or duplicate retired localization key: $key"
    }
}
foreach ($entry in $entries.PSObject.Properties) {
    if ($retiredSet.Contains($entry.Name)) { throw "Retired localization key cannot be published: $($entry.Name)" }
}
$pending = @()
foreach ($language in @('zh-CN', 'en-US')) {
    $path = Join-Path $Destination ($language + '.json')
    $content = [IO.File]::ReadAllText($path)
    # 先解析输入，防止删除操作掩盖原始损坏；两语言全部完成校验后才写入。
    $null = ConvertFrom-Json -InputObject $content
    $newline = if ($content.Contains("`r`n")) { "`r`n" } else { "`n" }
    foreach ($key in $retiredKeys) {
        $keyJson = ConvertTo-Json -InputObject $key -Compress
        $pattern = '(?m)^[ \t]*' + [regex]::Escape($keyJson) + '[ \t]*:[ \t]*"(?:\\.|[^"\\])*"[ \t]*,?[ \t]*(?:\r?\n|$)'
        $content = [regex]::Replace($content, $pattern, '')
    }
    # 删除最后一项后移除悬空逗号，保留相邻行和文件使用的换行形式。
    $content = [regex]::Replace($content, '(?m),(?<spacing>\s*)}(?=\s*$)', '${spacing}}')
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
            $prefix = $content.Substring(0, $end).TrimEnd()
            $separator = if ($prefix.EndsWith('{')) { '' } else { ',' }
            $content = $prefix + $separator + $newline + $line + $newline + '}' + $newline
        }
    }
    $published = ConvertFrom-Json -InputObject $content
    foreach ($key in $retiredKeys) {
        if ($published.PSObject.Properties.Name -ccontains $key) { throw "Retired localization key was not removed: $key" }
    }
    $pending += @{ Path = $path; Content = $content }
}
foreach ($item in $pending) {
    if ([IO.File]::ReadAllText($item.Path) -ne $item.Content) {
        [IO.File]::WriteAllText($item.Path, $item.Content, (New-Object Text.UTF8Encoding($false)))
    }
}
Write-Output 'Localization published: zh-CN, en-US.'
