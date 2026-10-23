# 联机武器命中反馈 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 双人复用单人现有命中、撞墙和武器爆炸反馈。

**Architecture:** 服务器只在真实弹丸结算处记录有界瞬时事件，协议携带位置、方向、半径和时间。客户端按轮次、序列和时效消费，复用既有表现端口。

**Tech Stack:** Unity / C# / MemoryPack / 既有协议生成工具。

**Spec:** `Docs/Plans/执行总清单.md` 的“双人武器命中与爆炸反馈补齐”。

## Global Constraints

- 只做第一项，篝火预警和第一人称暂缓；不改伤害、射速及资源。
- 临时动态预览先展示，正式表现接线等待明确确认；独立同步功能继续。
- 当前共享工作区实施，仅提交本项差异；不打包，测试明确范围、上限及 includePassingTests=true。

## Review Focus

- 重复快照只播一次；客户端基线和重连不补播旧反馈。
- 轮次倒退、隐藏区域、暂停及延迟包不播放陈旧事件。
- 到期回收普通子弹不产生伪命中，火焰伤害不冒充枪弹命中。
- 紫火箭和紫射线独立路径、箱体及桶碰撞不遗漏或双播。
- 消息深复制、双端生成一致，列表容量有界。

### Task 1: 权威事件与协议

**Files:** 新建服务器 WeaponFeedback partial 和 WeaponFeedbackOnlineChecks；修改弹丸碰撞入口、运行态、协议及生成输出。

**Interfaces:** 新增 OnlineArcadeWeaponFeedbackInfo，Kind=1身体/2表面/3爆炸；Sequence、RoundGeneration、PositionX/Z、DirectionX/Z、Radius、ServerTime。

- [x] 在真实 TickProjectiles 上写定向失败检查，先确认伤害生效但反馈缺失。
- [x] 添加协议字段并运行 Tools/gen-protocol.ps1；碰撞处记录，容量128，切轮清理且序列保持战斗内单调。
- [x] 运行 WeaponFeedbackOnline（最多15项，includePassingTests=true），覆盖上列服务器边界并验证快照复制及编码。

### Task 2: 客户端消费与预览

**Files:** 新建 OnlineWeaponFeedbackPresentationState 及 EditMode 定向测试；修改只读快照、映射和表现系统。

**Interfaces:** 只读事件由 Observe 消费，回调调用现有3个表现端口；基线、旧轮、过期事件仅标记已消费。

- [x] 制作现有单人效果及精简候选动态预览；用户明确采用右下并要求单双人统一。
- [x] 先写并运行去重/时效失败测试，再实现只读映射与消费。
- [x] 视觉确认后接入现有表现端口；指定 OnlineWeaponFeedbackTests（最多11项），包含正式路由和池复用。

### Task 3: 验证与交付

- [x] 双端编译及必要定向弹丸回归；独立审查本项改动并修复发现，证据见交付README。
- [x] 更新总清单、计划及本项提交范围；按项目规则自动中文提交、推送，实际交付以Git提交及远端记录为准。
