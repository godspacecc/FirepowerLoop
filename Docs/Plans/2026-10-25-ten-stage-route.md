# 十关新局路线 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 正式新局仅走海岛1→码头21→农场22→墓园23→神庙24→集市25→巨石5→要塞7→锦鲤10→终局20，共十场四十普通轮；旧局保持原路线与计数。

**Architecture:** 复用ArcadeRouteRules作为客户端和服务端共同的版本出口投影；新版本2读取权威配置，旧版本0/1显式保留被本次改动覆盖的旧边。结算、战斗准备、轮前恢复及奖励门继续走现有领域事务，不新增表现或保存分区。

**Tech Stack:** Unity C#、QFramework、Luban、.NET 8服务端限定检查。

**Spec:** [执行总清单：十关完整内容收口](执行总清单.md)，2026-10-25用户已明确采用精确名单并授权实施。视觉不在本切片范围，火山仍暂停。

## 约束与验收

- 不创建worktree，不改美术，不触发Player Build；保留其他会话全部现有差异。
- 配置只改Config/DataCsv/arcade_arena.csv及arcade_rule.csv；主代理统一执行Config/gen.bat，不手改生成输出或协议。
- v0十场与v1十三场的三种旧分支、挑战返回及胜利结果仍可恢复；不重新解释旧CasualRouteVersion。
- 实际四轮、宝箱、跨轮/整场出口、终局Boss和结果保存由限定客户端/服务端检查覆盖。
- 日志统一Backup/Artifacts/TenStageRouteApplied-20261025；短类TenStageRouteTests最多12项、TenStageRouteOnline最多8项，includePassingTests=true；无效过滤退出2。

## 执行

- [x] 读取当前领域实现、配置权威源、总清单和现有保存/服务端验证入口。
- [x] 添加先行限定客户端测试TenStageRouteTests（11项展开）；主代理实际RED为5过6失败，失败原因分别为开局仍版本1、生成后继仍含旧关和版本2节点被拒绝。
- [x] 添加独立服务端TenStageRouteOnline检查（8项固定），主代理统一执行，未经子代理运行。
- [x] 根据真实RED应用版本2、旧边映射、配置和必要的兼容性修复；新增环境5不再影响服务端旧顺序1—20过滤，业务EarlyStage解耦由巨石子任务负责。
- [x] 主代理统一Config/gen.bat成功（36/30），TenStageRouteTests 11/11 GREEN；TenStageRouteOnline 8/8 GREEN并确认进程exit0，覆盖实际四十轮、旧三分支、轮前保存／恢复、Boss／奖励／结果和已知指纹。
- [x] 两位只读审查者分别核对路线/Creature交叉与Henge事务，原问题关闭；证据已登记，主代理按项目规则选择性中文提交推送。

## 实际验证记录

- 客户端RED：11项中5过6失败，失败证明新局仍为版本1、配置5仍去6、版本2存档／四十轮准备被拒绝；参见本批client-red.json。
- 服务端RED：8项中4过4失败。新局版本、四十轮轮前事务、已知指纹迁移出现预期业务失败；VictoryCountsKeepSavedVersion被独立工具未初始化ET.Logger掩盖为空引用，该项不记为有效业务RED。工具已补显式Console日志端口，后续失败保留真实保存原因。
- 主代理已执行Config/gen.bat成功，36客户端／30服务端表；本子代理没有执行生成或修改生成代码／bytes。
- 主代理确认客户端11/11、服务端8/8通过且服务端exit0；日志位于本批client-green.json与server-green.log。旧十三场限定检查修订后的新回归由主代理统一安排；路线的新旧版本三分支已在本批11+8项中覆盖。
- 原十三场测试已改为显式ExpandedVersion=1并真实文件恢复，保留旧局十三场五十二轮语义，不以新局开局假设继续断言旧值。
