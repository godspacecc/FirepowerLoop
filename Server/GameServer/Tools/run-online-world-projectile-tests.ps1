$ErrorActionPreference = 'Stop'
if (Get-NetTCPConnection -LocalPort 31011 -State Listen -ErrorAction SilentlyContinue) { throw '弓箭定向测试端口 31011 已被占用；未启动或停止任何服务器。' }
# 专用入口始终锁定弓箭用例，不通过空 Mode 回退到完整联机套件。
& (Join-Path $PSScriptRoot 'run-online-world-tests.ps1') -Mode WorldProjectiles
exit $LASTEXITCODE
