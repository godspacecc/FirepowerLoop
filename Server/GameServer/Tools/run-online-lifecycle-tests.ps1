param(
    [string]$Address = "127.0.0.1:31011",
    [string]$UserName = "",
    [string]$StorePath = "",
    [int]$StartupTimeoutSeconds = 20,
    [switch]$StartStopSelfCheck,
    [switch]$ReplaceQuickOnly,
    [switch]$LoginEntryOnly
)

$ErrorActionPreference = "Stop"

# 账号生产入口已迁入Mongo；旧JSON写失败注入不再适用于现行服务。
if ($StartStopSelfCheck -or $ReplaceQuickOnly -or $LoginEntryOnly -or $StorePath -or $UserName -or $Address -ne "127.0.0.1:31011") {
    throw "旧JSON生命周期专用选项已退役；请使用 Tools/NetworkFoundationChecks/run-account-foundation.ps1 的隔离Mongo检查，房间玩法使用对应专项入口。"
}
& (Join-Path $PSScriptRoot '../../../Tools/NetworkFoundationChecks/run-account-foundation.ps1')
return
